using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Entities;
using MV.DomainLayer.Exceptions;
using MV.DomainLayer.Helpers;
using MV.ApplicationLayer.Interfaces;
using static MV.DomainLayer.Constants.ClassSessionStatus;
namespace MV.ApplicationLayer.Services;

/// <summary>
/// Service for settlement and escrow management
/// </summary>
public class SettlementService : ISettlementService
{
    private readonly IAppDbContext _context;
    private readonly INotificationService _notificationService;
    private readonly ILogger<SettlementService> _logger;

    public SettlementService(
        IAppDbContext context,
        INotificationService notificationService,
        ILogger<SettlementService> logger)
    {
        _context = context;
        _notificationService = notificationService;
        _logger = logger;
    }

    /// <summary>
    /// Process auto-confirm for classSessions past their deadline (called by background job)
    /// </summary>
    public async Task<int> ProcessAutoConfirmAsync(CancellationToken ct = default)
    {
        var now = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;

        var classSessionsToConfirm = await _context.ClassSessions
            .Where(l => l.Status == PendingConfirmation &&
                        l.Confirmdeadline.HasValue &&
                        l.Confirmdeadline.Value <= now &&
                        l.Issettled != true &&
                        !_context.Disputes.Any(d => d.Classsessionid == l.Classsessionid && d.Status != DisputeStatus.Resolved && d.Status != DisputeStatus.Closed))
            .Include(l => l.Booking)
            .ToListAsync(ct);

        var confirmedCount = 0;

        foreach (var classSession in classSessionsToConfirm)
        {
            try
            {
                await SettleClassSessionInternalAsync(classSession, null, SettlementType.AutoConfirm);
                confirmedCount++;
                _logger.LogInformation("Auto-confirmed classSession {ClassSessionId}", classSession.Classsessionid);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to auto-confirm classSession {ClassSessionId}", classSession.Classsessionid);
            }
        }

        if (confirmedCount > 0)
            _logger.LogInformation("Auto-confirmed {Count} classSessions", confirmedCount);

        return confirmedCount;
    }

    /// <summary>
    /// Settle a specific classSession - move money from frozen to tutor's balance
    /// </summary>
    public async Task<SettlementResultResponse> SettleClassSessionAsync(int classSessionId, string? confirmedBy = null)
    {
        var classSession = await _context.ClassSessions
            .Include(l => l.Booking)
            .FirstOrDefaultAsync(l => l.Classsessionid == classSessionId)
            ?? throw new ClassSessionException(ClassSessionErrorCodes.ClassSessionNotFound, "Không tìm thấy buổi học", 404);

        if (classSession.Issettled == true)
            throw new ClassSessionException(ClassSessionErrorCodes.ClassSessionAlreadyConfirmed, "Buổi học này đã được xác nhận rồi", 400);

        if (classSession.Status != PendingConfirmation && classSession.Status != Completed)
            throw new ClassSessionException(ClassSessionErrorCodes.InvalidClassSessionStatus, "Buổi học chưa ở trạng thái sẵn sàng để thanh toán", 400);

        return await SettleClassSessionInternalAsync(classSession, confirmedBy, SettlementType.Manual);
    }

    private async Task<SettlementResultResponse> SettleClassSessionInternalAsync(ClassSession classSession, string? confirmedBy, string settlementType)
    {
        await using var tx = await _context.Database.BeginTransactionAsync();
        try
        {
            var tutorId = classSession.Tutorid;
            var classSessionPrice = classSession.Lessonprice ?? 0;

            // Get tutor's wallet
            var tutorWallet = await _context.Wallets
                .FromSqlRaw(SqlQueries.LockWalletByUserId, tutorId)
                            .FirstOrDefaultAsync();

            if (tutorWallet == null)
            {
                tutorWallet = new Wallet
                {
                    Userid = tutorId,
                    Balance = 0,
                    Frozenbalance = 0,
                    Lastupdated = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow
                };
                _context.Wallets.Add(tutorWallet);
            }

            // Update classSession status
            classSession.Status = Completed;
            classSession.Issettled = true;
            classSession.Parentackat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;

            bool isBookingCompleted = false;
            // Update booking sessions remaining
            if (classSession.Booking != null && (classSession.Booking.Sessionsremaining ?? 0) > 0)
            {
                classSession.Booking.Sessionsremaining -= 1;
                if (classSession.Booking.Sessionsremaining <= 0)
                {
                    classSession.Booking.Status = BookingStatus.Completed;
                    isBookingCompleted = true;
                }
            }

            decimal amountReleasedNow = 0;
            long? transactionId = null;

            if (isBookingCompleted)
            {
                var previousEarned = await _context.ClassSessions
                    .Where(l => l.Bookingid == classSession.Bookingid && l.Classsessionid != classSession.Classsessionid && l.Status == Completed)
                    .SumAsync(l => l.Lessonprice ?? 0);
                var totalEarned = previousEarned + classSessionPrice;
                var currentFrozen = tutorWallet.Frozenbalance ?? 0;
                if (currentFrozen < totalEarned)
                {
                    _logger.LogError(
                        "Tutor {TutorId} frozen balance {Frozen} is less than total earned {Total} for booking {BookingId}. " +
                        "Using available frozen balance instead to prevent negative.",
                        tutorId, currentFrozen, totalEarned, classSession.Bookingid);
                    // Giải ngân toàn bộ số dư frozen còn lại (không cho âm)
                    totalEarned = currentFrozen;
                }
                tutorWallet.Frozenbalance = Math.Max(0, currentFrozen - totalEarned);
                tutorWallet.Balance += totalEarned;
                tutorWallet.Lastupdated = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
                amountReleasedNow = totalEarned;
                var transaction = new Wallettransaction
                {
                    Walletid = tutorWallet.Walletid,
                    Amount = totalEarned,
                    Transactiontype = TransactionType.EscrowRelease,
                    Description = $"Thanh toán hoàn tất khóa học #{classSession.Bookingid}",
                    Createdat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow
                };
                _context.Wallettransactions.Add(transaction);
                await _context.SaveChangesAsync();
                transactionId = transaction.Transactionid;
            }
            else
            {
                _logger.LogInformation("ClassSession {ClassSessionId} settled, but funds held in escrow until booking is completed", classSession.Classsessionid);
            }

            await _context.SaveChangesAsync();
            await tx.CommitAsync();

            _logger.LogInformation("Settled classSession {ClassSessionId}, amount {Amount} released to tutor {TutorId}",
                classSession.Classsessionid, amountReleasedNow, tutorId);

            // Send notification
            try
            {
                if (isBookingCompleted)
                {
                    await _notificationService.CreateNotificationAsync(new NotificationRequest
                    {
                        Userid = tutorId,
                        Title = "Giải ngân khóa học thành công",
                        Message = $"Khóa học #{classSession.Bookingid} đã hoàn thành. Bạn đã nhận được tổng cộng {amountReleasedNow:N0}đ. Số dư ví hiện tại: {tutorWallet.Balance:N0}đ"
                    });
                }
                else
                {
                    await _notificationService.CreateNotificationAsync(new NotificationRequest
                    {
                        Userid = tutorId,
                        Title = "Xác nhận buổi học",
                        Message = $"Buổi học #{classSession.Classsessionid} đã được xác nhận. Tiền học sẽ được giải ngân khi hoàn thành toàn bộ khóa học."
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send settlement notification for classSession {ClassSessionId}", classSession.Classsessionid);
            }

            return new SettlementResultResponse
            {
                ClassSessionId = classSession.Classsessionid,
                BookingId = classSession.Bookingid,
                Success = true,
                Message = isBookingCompleted
                    ? "Khóa học đã hoàn thành và toàn bộ tiền đã được giải ngân cho gia sư"
                    : "Buổi học đã được xác nhận thành công (tiền học sẽ được giải ngân khi kết thúc toàn bộ khóa học)",
                AmountReleased = amountReleasedNow,
                AmountRefunded = 0,
                SettlementType = isBookingCompleted ? SettlementType.FullRelease : SettlementType.LessonConfirmed,
                TransactionId = transactionId ?? 0,
                NewTutorBalance = tutorWallet.Balance,
                SessionsRemaining = classSession.Booking?.Sessionsremaining
            };
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    /// <summary>
    /// Process refund for a classSession
    /// </summary>
    public async Task<SettlementResultResponse> ProcessRefundAsync(int classSessionId, int refundPercentage, string processedBy)
    {
        if (refundPercentage < 0 || refundPercentage > 100)
            throw new ArgumentException("Phần trăm hoàn tiền phải nằm trong khoảng từ 0 đến 100");

        var classSession = await _context.ClassSessions
            .Include(l => l.Booking)
            .FirstOrDefaultAsync(l => l.Classsessionid == classSessionId)
            ?? throw new ClassSessionException(ClassSessionErrorCodes.ClassSessionNotFound, "Không tìm thấy buổi học", 404);

        if (classSession.Issettled == true)
            throw new ClassSessionException(ClassSessionErrorCodes.ClassSessionAlreadyConfirmed, "Buổi học này đã được xác nhận rồi", 400);

        await using var tx = await _context.Database.BeginTransactionAsync();
        try
        {
            var classSessionPrice = classSession.Lessonprice ?? 0;
            var refundAmount = classSessionPrice * refundPercentage / 100;
            var tutorAmount = classSessionPrice - refundAmount;
            var parentId = classSession.Booking?.Parentid;
            var tutorId = classSession.Tutorid;

            // Get wallets
            var tutorWallet = await _context.Wallets
                .FromSqlRaw(SqlQueries.LockWalletByUserId, tutorId)
                            .FirstOrDefaultAsync();

            var parentWallet = parentId != null
                ? await _context.Wallets
                .FromSqlRaw(SqlQueries.LockWalletByUserId, parentId)
                    .FirstOrDefaultAsync()
                : null;

            // Deduct from frozen balance
            if (tutorWallet != null)
            {
                if ((tutorWallet.Frozenbalance ?? 0) < classSessionPrice)
                    _logger.LogWarning("Tutor {TutorId} frozen balance {Frozen} is less than classSession price {Price} for refund classSession {ClassSessionId}",
                        tutorId, tutorWallet.Frozenbalance, classSessionPrice, classSession.Classsessionid);
                tutorWallet.Frozenbalance = (tutorWallet.Frozenbalance ?? 0) - classSessionPrice;
                if (tutorAmount > 0)
                {
                    tutorWallet.Balance += tutorAmount;
                }
                tutorWallet.Lastupdated = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
            }

            // Add refund to parent's balance
            if (parentWallet != null && refundAmount > 0)
            {
                parentWallet.Balance += refundAmount;
                parentWallet.Lastupdated = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;

                // Create refund transaction
                var refundTx = new Wallettransaction
                {
                    Walletid = parentWallet.Walletid,
                    Amount = refundAmount,
                    Transactiontype = TransactionType.Refund,
                    Description = $"Hoàn tiền buổi học #{classSession.Classsessionid} ({refundPercentage}%)",
                    Createdat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow
                };
                _context.Wallettransactions.Add(refundTx);

                // Notify Parent
                await _notificationService.CreateNotificationAsync(new NotificationRequest
                {
                    Userid = parentId!,
                    Title = "Hoàn tiền buổi học",
                    Message = $"Bạn đã nhận được hoàn tiền {refundAmount:N0}đ ({refundPercentage}%) cho buổi học #{classSession.Classsessionid}. Số dư ví: {parentWallet.Balance:N0}đ"
                });
            }

            // Update classSession
            bool isClassSessionCancelled = refundPercentage == 100;
            classSession.Status = isClassSessionCancelled ? Cancelled : Completed;
            classSession.Issettled = true;

            if (isClassSessionCancelled && classSession.Booking != null)
            {
                classSession.Booking.Sessionsremaining = (classSession.Booking.Sessionsremaining ?? 0) + 1;
                _logger.LogInformation(
                    "Restored 1 session to booking {BookingId}. New sessionsremaining: {Count}",
                    classSession.Bookingid, classSession.Booking.Sessionsremaining);
            }

            await _context.SaveChangesAsync();
            await tx.CommitAsync();

            var settlementType = refundPercentage switch
            {
                100 => SettlementType.FullRefund,
                50 => SettlementType.Refund50,
                _ => $"refund_{refundPercentage}"
            };

            _logger.LogInformation("Processed refund for classSession {ClassSessionId}: {RefundPercent}% ({RefundAmount})",
                classSession.Classsessionid, refundPercentage, refundAmount);

            return new SettlementResultResponse
            {
                ClassSessionId = classSession.Classsessionid,
                BookingId = classSession.Bookingid,
                Success = true,
                Message = $"Đã hoàn {refundPercentage}% học phí cho buổi học #{classSession.Classsessionid}",
                AmountReleased = tutorAmount,
                AmountRefunded = refundAmount,
                SettlementType = settlementType,
                NewTutorBalance = tutorWallet?.Balance
            };
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    // Múi giờ Việt Nam (UTC+7) – dùng cho response hiển thị (now using VietnamTimeHelper)

    /// <summary>
    /// Get classSessions pending settlement (for admin view)
    /// </summary>
    public async Task<List<PendingClassSessionResponse>> GetPendingSettlementsAsync()
    {
        var now = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;

        var classSessions = await _context.ClassSessions
            .AsNoTracking()
            .Where(l => l.Status == PendingConfirmation && l.Issettled != true)
            .Include(l => l.Booking)
                .ThenInclude(b => b!.Tutorsubjectgradeprice)
                    .ThenInclude(p => p!.Subject)
            .Include(l => l.Booking)
                .ThenInclude(b => b!.Student)
            .Include(l => l.Tutor)
                .ThenInclude(t => t!.Tutor)
            .OrderBy(l => l.Confirmdeadline)
            .ToListAsync();

        return classSessions.Select(l => new PendingClassSessionResponse
        {
            ClassSessionId = l.Classsessionid,
            BookingId = l.Bookingid,
            ScheduledStart = l.Scheduledstart,
            ScheduledEnd = l.Scheduledend,
            SubmittedAt = l.Submittedat,
            ConfirmDeadline = l.Confirmdeadline,
            TutorName = l.Tutor?.Tutor?.Fullname,
            TutorAvatarUrl = l.Tutor?.Tutor?.Avatarurl,
            StudentName = l.Booking?.Student?.Fullname,
            SubjectName = l.Booking?.Subject?.Subjectname,
            ClassSessionPrice = l.Lessonprice,
            ClassSessionContent = l.Lessoncontent,
            Homework = l.Homework,
            TutorNotes = l.Tutornotes
        }).ToList();
    }

    /// <summary>
    /// Finalize booking early: parent did not pay for remaining sessions.
    /// Releases escrow for completed classSessions, cancels pending classSessions, marks booking Completed.
    /// </summary>
    public async Task FinalizeBookingEarlyAsync(int bookingId, CancellationToken ct = default)
    {
        var booking = await _context.Bookings
            .Include(b => b.ClassSessions)
            .FirstOrDefaultAsync(b => b.Bookingid == bookingId, ct)
            ?? throw new InvalidOperationException($"Booking {bookingId} not found");

        var tutorId = booking.Tutorid;
        if (string.IsNullOrWhiteSpace(tutorId)) return;

        await using var tx = await _context.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable, ct);
        try
        {
            var tutorWallet = await _context.Wallets
                .FromSqlRaw(MV.DomainLayer.Constants.SqlQueries.LockWalletByUserId, tutorId)
                .FirstOrDefaultAsync(ct);

            if (tutorWallet == null)
            {
                tutorWallet = new Wallet
                {
                    Userid = tutorId,
                    Balance = 0,
                    Frozenbalance = 0,
                    Lastupdated = TimeZoneHelper.UtcNow
                };
                _context.Wallets.Add(tutorWallet);
            }

            // Count completed classSessions and calculate escrow to release
            var completedClassSessions = booking.ClassSessions!
                .Where(l => l.Status == Completed || l.Issettled == true)
                .ToList();
            var completedCount = completedClassSessions.Count;

            var totalSessions = booking.Totalsessions ?? 1;
            var totalEscrow = booking.Tutorfee ?? 0;
            // Release only the escrow corresponding to classSessions already completed
            var perClassSession = Math.Round(totalEscrow / totalSessions, 2);
            var releaseAmount = Math.Min(perClassSession * completedCount, tutorWallet.Frozenbalance ?? 0);

            if (releaseAmount > 0)
            {
                tutorWallet.Frozenbalance = Math.Max(0, (tutorWallet.Frozenbalance ?? 0) - releaseAmount);
                tutorWallet.Balance = (tutorWallet.Balance ?? 0) + releaseAmount;
                tutorWallet.Lastupdated = TimeZoneHelper.UtcNow;

                _context.Wallettransactions.Add(new Wallettransaction
                {
                    Walletid = tutorWallet.Walletid,
                    Amount = releaseAmount,
                    Transactiontype = TransactionType.EscrowRelease,
                    Referencetable = ReferenceTable.Booking,
                    Referenceid = bookingId,
                    Description = $"Kết thúc sớm — đã dạy {completedCount}/{totalSessions} buổi #{bookingId}",
                    Createdat = TimeZoneHelper.UtcNow
                });
            }

            // Cancel classSessions that have not started yet
            var now = TimeZoneHelper.UtcNow;
            foreach (var classSession in booking.ClassSessions!)
            {
                if (classSession.Status == ClassSessionStatus.Reserved || classSession.Status == ClassSessionStatus.Scheduled)
                {
                    classSession.Status = ClassSessionStatus.Cancelled;
                }
            }

            // Mark booking as completed
            booking.Status = BookingStatus.Completed;
            booking.Sessionsremaining = 0;
            booking.Updatedat = now;

            await _context.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            _logger.LogInformation(
                "FinalizeBookingEarly: booking {BookingId} finalized. Released {Amount} for {Completed}/{Total} completed classSessions.",
                bookingId, releaseAmount, completedCount, totalSessions);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }

        // Notifications (outside transaction)
        try
        {
            var completedClassSessionsCount = booking.ClassSessions!.Count(l => l.Status == Completed || l.Issettled == true);
            await _notificationService.CreateNotificationAsync(new NotificationRequest
            {
                Userid = tutorId,
                Title = "Phụ huynh ngừng học — giải ngân hoàn tất",
                Message = $"Booking #{bookingId}: Phụ huynh không thanh toán các buổi còn lại. " +
                          $"Bạn đã nhận thanh toán cho {completedClassSessionsCount} buổi đã dạy.",
                Type = NotificationType.BookingTimeout,
                Referenceid = bookingId.ToString()
            });

            if (!string.IsNullOrWhiteSpace(booking.Parentid))
            {
                await _notificationService.CreateNotificationAsync(new NotificationRequest
                {
                    Userid = booking.Parentid,
                    Title = "Khóa học đã kết thúc",
                    Message = $"Booking #{bookingId} đã kết thúc sau buổi học đầu tiên do không thanh toán các buổi còn lại.",
                    Type = NotificationType.BookingTimeout,
                    Referenceid = bookingId.ToString()
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send finalize-early notifications for booking {BookingId}", bookingId);
        }
    }
}
