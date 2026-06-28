using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.Helpers;
using MV.ApplicationLayer.Interfaces;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Entities;
using MV.DomainLayer.Exceptions;
using MV.DomainLayer.Helpers;
using static MV.DomainLayer.Constants.LessonStatus;
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
    /// Process auto-confirm for lessons past their deadline (called by background job)
    /// </summary>
    public async Task<int> ProcessAutoConfirmAsync(CancellationToken ct = default)
    {
        var now = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;

        var lessonsToConfirm = await _context.Lessons
            .Where(l => l.Status == PendingConfirmation &&
                        l.Confirmdeadline.HasValue &&
                        l.Confirmdeadline.Value <= now &&
                        l.Issettled != true &&
                        !_context.Disputes.Any(d => d.Lessonid == l.Lessonid && d.Status != DisputeStatus.Resolved && d.Status != DisputeStatus.Closed))
            .Include(l => l.Booking)
            .ToListAsync(ct);

        var confirmedCount = 0;

        foreach (var lesson in lessonsToConfirm)
        {
            try
            {
                await SettleLessonInternalAsync(lesson, null, SettlementType.AutoConfirm);
                confirmedCount++;
                _logger.LogInformation("Auto-confirmed lesson {LessonId}", lesson.Lessonid);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to auto-confirm lesson {LessonId}", lesson.Lessonid);
            }
        }

        if (confirmedCount > 0)
            _logger.LogInformation("Auto-confirmed {Count} lessons", confirmedCount);

        return confirmedCount;
    }

    /// <summary>
    /// Settle a specific lesson - move money from frozen to tutor's balance
    /// </summary>
    public async Task<SettlementResultResponse> SettleLessonAsync(int lessonId, string? confirmedBy = null)
    {
        var lesson = await _context.Lessons
            .Include(l => l.Booking)
            .FirstOrDefaultAsync(l => l.Lessonid == lessonId)
            ?? throw new LessonException(LessonErrorCodes.LessonNotFound, "Không tìm thấy buổi học", 404);

        if (lesson.Issettled == true)
            throw new LessonException(LessonErrorCodes.LessonAlreadyConfirmed, "Buổi học này đã được xác nhận rồi", 400);

        if (lesson.Status != PendingConfirmation && lesson.Status != Completed)
            throw new LessonException(LessonErrorCodes.InvalidLessonStatus, "Buổi học chưa ở trạng thái sẵn sàng để thanh toán", 400);

        return await SettleLessonInternalAsync(lesson, confirmedBy, SettlementType.Manual);
    }

    private async Task<SettlementResultResponse> SettleLessonInternalAsync(Lesson lesson, string? confirmedBy, string settlementType)
    {
        await using var tx = await _context.Database.BeginTransactionAsync();
        try
        {
            var tutorId = lesson.Tutorid;
            var lessonPrice = lesson.Lessonprice ?? 0;

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

            // Update lesson status
            lesson.Status = Completed;
            lesson.Issettled = true;
            lesson.Parentackat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;

            bool isBookingCompleted = false;
            // Update booking sessions remaining
            if (lesson.Booking != null && (lesson.Booking.Sessionsremaining ?? 0) > 0)
            {
                lesson.Booking.Sessionsremaining -= 1;
                if (lesson.Booking.Sessionsremaining <= 0)
                {
                    lesson.Booking.Status = BookingStatus.Completed;
                    isBookingCompleted = true;
                }
            }

            decimal amountReleasedNow = 0;
            long? transactionId = null;

            if (isBookingCompleted)
            {
                var previousEarned = await _context.Lessons
                    .Where(l => l.Bookingid == lesson.Bookingid && l.Lessonid != lesson.Lessonid && l.Status == Completed)
                    .SumAsync(l => l.Lessonprice ?? 0);
                var totalEarned = previousEarned + lessonPrice;
                var currentFrozen = tutorWallet.Frozenbalance ?? 0;
                if (currentFrozen < totalEarned)
                {
                    _logger.LogError(
                        "Tutor {TutorId} frozen balance {Frozen} is less than total earned {Total} for booking {BookingId}. " +
                        "Using available frozen balance instead to prevent negative.",
                        tutorId, currentFrozen, totalEarned, lesson.Bookingid);
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
                    Description = $"Thanh toán hoàn tất khóa học #{lesson.Bookingid}",
                    Createdat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow
                };
                _context.Wallettransactions.Add(transaction);
                await _context.SaveChangesAsync();
                transactionId = transaction.Transactionid;
            }
            else
            {
                _logger.LogInformation("Lesson {LessonId} settled, but funds held in escrow until booking is completed", lesson.Lessonid);
            }

            await _context.SaveChangesAsync();
            await tx.CommitAsync();

            _logger.LogInformation("Settled lesson {LessonId}, amount {Amount} released to tutor {TutorId}",
                lesson.Lessonid, amountReleasedNow, tutorId);

            // Send notification
            try
            {
                if (isBookingCompleted)
                {
                    await _notificationService.CreateNotificationAsync(new NotificationRequest
                    {
                        Userid = tutorId,
                        Title = "Giải ngân khóa học thành công",
                        Message = $"Khóa học #{lesson.Bookingid} đã hoàn thành. Bạn đã nhận được tổng cộng {amountReleasedNow:N0}đ. Số dư ví hiện tại: {tutorWallet.Balance:N0}đ"
                    });
                }
                else
                {
                    await _notificationService.CreateNotificationAsync(new NotificationRequest
                    {
                        Userid = tutorId,
                        Title = "Xác nhận buổi học",
                        Message = $"Buổi học #{lesson.Lessonid} đã được xác nhận. Tiền học sẽ được giải ngân khi hoàn thành toàn bộ khóa học."
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send settlement notification for lesson {LessonId}", lesson.Lessonid);
            }

            return new SettlementResultResponse
            {
                LessonId = lesson.Lessonid,
                BookingId = lesson.Bookingid,
                Success = true,
                Message = isBookingCompleted
                    ? "Khóa học đã hoàn thành và toàn bộ tiền đã được giải ngân cho gia sư"
                    : "Buổi học đã được xác nhận thành công (tiền học sẽ được giải ngân khi kết thúc toàn bộ khóa học)",
                AmountReleased = amountReleasedNow,
                AmountRefunded = 0,
                SettlementType = isBookingCompleted ? SettlementType.FullRelease : SettlementType.LessonConfirmed,
                TransactionId = transactionId ?? 0,
                NewTutorBalance = tutorWallet.Balance,
                SessionsRemaining = lesson.Booking?.Sessionsremaining
            };
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    /// <summary>
    /// Process refund for a lesson
    /// </summary>
    public async Task<SettlementResultResponse> ProcessRefundAsync(int lessonId, int refundPercentage, string processedBy)
    {
        if (refundPercentage < 0 || refundPercentage > 100)
            throw new ArgumentException("Phần trăm hoàn tiền phải nằm trong khoảng từ 0 đến 100");

        var lesson = await _context.Lessons
            .Include(l => l.Booking)
            .FirstOrDefaultAsync(l => l.Lessonid == lessonId)
            ?? throw new LessonException(LessonErrorCodes.LessonNotFound, "Không tìm thấy buổi học", 404);

        if (lesson.Issettled == true)
            throw new LessonException(LessonErrorCodes.LessonAlreadyConfirmed, "Buổi học này đã được xác nhận rồi", 400);

        await using var tx = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        try
        {
            var booking = lesson.Booking
                ?? throw new LessonException(LessonErrorCodes.LessonNotFound, "Buổi học không có thông tin booking để tính hoàn tiền", 400);
            var parentRefundPerSession = LessonRefundCalculator.ParentRefundPerSession(booking);
            var tutorEscrowPerSession = LessonRefundCalculator.TutorEscrowPerSession(booking);
            var refundAmount = Math.Round(parentRefundPerSession * refundPercentage / 100m, 2);
            var tutorAmount = Math.Round(tutorEscrowPerSession * (100 - refundPercentage) / 100m, 2);
            var parentId = lesson.Booking?.Parentid;
            var tutorId = lesson.Tutorid;

            // Get wallets (with row lock for concurrency safety)
            var tutorWallet = await _context.Wallets
                .FromSqlRaw(SqlQueries.LockWalletByUserId, tutorId)
                .FirstOrDefaultAsync();

            var parentWallet = parentId != null
                ? await _context.Wallets
                    .FromSqlRaw(SqlQueries.LockWalletByUserId, parentId)
                    .FirstOrDefaultAsync()
                : null;

            // Release exactly one session's worth of escrow from tutor frozen balance
            if (tutorWallet != null)
            {
                if ((tutorWallet.Frozenbalance ?? 0) < tutorEscrowPerSession)
                    _logger.LogWarning(
                        "Tutor {TutorId} frozen balance {Frozen} less than escrow per session {Escrow} for lesson {LessonId}",
                        tutorId, tutorWallet.Frozenbalance, tutorEscrowPerSession, lesson.Lessonid);
                tutorWallet.Frozenbalance = Math.Max(0, (tutorWallet.Frozenbalance ?? 0) - tutorEscrowPerSession);
                if (tutorAmount > 0)
                    tutorWallet.Balance += tutorAmount;
                tutorWallet.Lastupdated = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
            }

            // Credit parent wallet with what they actually paid (Finalprice-based, not Lessonprice)
            if (parentWallet != null && refundAmount > 0)
            {
                parentWallet.Balance += refundAmount;
                parentWallet.Lastupdated = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;

                _context.Wallettransactions.Add(new Wallettransaction
                {
                    Walletid = parentWallet.Walletid,
                    Amount = refundAmount,
                    Transactiontype = TransactionType.Refund,
                    Referencetable = ReferenceTable.Booking,
                    Referenceid = lesson.Bookingid,
                    Description = $"Hoàn tiền buổi học #{lesson.Lessonid} ({refundPercentage}%)",
                    Createdat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow
                });
            }

            // Update lesson
            bool isLessonCancelled = refundPercentage == 100;
            lesson.Status = isLessonCancelled ? Cancelled : Completed;
            lesson.Issettled = true;

            if (isLessonCancelled && lesson.Booking != null)
            {
                lesson.Booking.Sessionsremaining = (lesson.Booking.Sessionsremaining ?? 0) + 1;
                _logger.LogInformation(
                    "Restored 1 session to booking {BookingId}. New sessionsremaining: {Count}",
                    lesson.Bookingid, lesson.Booking.Sessionsremaining);
            }

            await _context.SaveChangesAsync();
            await tx.CommitAsync();

            // Notify parent after commit (best-effort — must not roll back committed tx)
            try
            {
                if (parentId != null && refundAmount > 0)
                {
                    await _notificationService.CreateNotificationAsync(new NotificationRequest
                    {
                        Userid = parentId,
                        Title = "Hoàn tiền buổi học",
                        Message = $"Bạn đã nhận được hoàn tiền {refundAmount:N0}đ ({refundPercentage}%) cho buổi học #{lesson.Lessonid}. Số dư ví: {parentWallet?.Balance:N0}đ"
                    });
                }
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to send refund notification for lesson {LessonId}", lesson.Lessonid); }

            var settlementType = refundPercentage switch
            {
                100 => SettlementType.FullRefund,
                50 => SettlementType.Refund50,
                _ => $"refund_{refundPercentage}"
            };

            _logger.LogInformation("Processed refund for lesson {LessonId}: {RefundPercent}% (parent refund {RefundAmount}, tutor earns {TutorAmount})",
                lesson.Lessonid, refundPercentage, refundAmount, tutorAmount);

            return new SettlementResultResponse
            {
                LessonId = lesson.Lessonid,
                BookingId = lesson.Bookingid,
                Success = true,
                Message = $"Đã hoàn {refundPercentage}% học phí cho buổi học #{lesson.Lessonid}",
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
    /// Get lessons pending settlement (for admin view)
    /// </summary>
    public async Task<List<PendingLessonResponse>> GetPendingSettlementsAsync()
    {
        var now = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;

        var lessons = await _context.Lessons
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

        return lessons.Select(l => new PendingLessonResponse
        {
            LessonId = l.Lessonid,
            BookingId = l.Bookingid,
            ScheduledStart = l.Scheduledstart,
            ScheduledEnd = l.Scheduledend,
            SubmittedAt = l.Submittedat,
            ConfirmDeadline = l.Confirmdeadline,
            TutorName = l.Tutor?.Tutor?.Fullname,
            TutorAvatarUrl = l.Tutor?.Tutor?.Avatarurl,
            StudentName = l.Booking?.Student?.Fullname,
            SubjectName = l.Booking?.Subject?.Subjectname,
            LessonPrice = l.Lessonprice,
            LessonContent = l.Lessoncontent,
            Homework = l.Homework,
            TutorNotes = l.Tutornotes
        }).ToList();
    }

    /// <summary>
    /// Finalize booking early: parent did not pay for remaining sessions.
    /// Releases escrow for completed lessons, cancels pending lessons, marks booking Completed.
    /// </summary>
    public async Task FinalizeBookingEarlyAsync(int bookingId, CancellationToken ct = default)
    {
        var booking = await _context.Bookings
            .Include(b => b.Lessons)
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

            // Count completed lessons and calculate escrow to release
            var completedLessons = booking.Lessons!
                .Where(l => l.Status == Completed || l.Issettled == true)
                .ToList();
            var completedCount = completedLessons.Count;

            var totalSessions = booking.Totalsessions ?? 1;
            var totalEscrow = booking.Tutorfee ?? 0;
            // Release only the escrow corresponding to lessons already completed
            var perLesson = Math.Round(totalEscrow / totalSessions, 2);
            var releaseAmount = Math.Min(perLesson * completedCount, tutorWallet.Frozenbalance ?? 0);

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

            // Cancel lessons that have not started yet
            var now = TimeZoneHelper.UtcNow;
            foreach (var lesson in booking.Lessons!)
            {
                if (lesson.Status == LessonStatus.Reserved || lesson.Status == LessonStatus.Scheduled)
                {
                    lesson.Status = LessonStatus.Cancelled;
                }
            }

            // Mark booking as completed
            booking.Status = BookingStatus.Completed;
            booking.Sessionsremaining = 0;
            booking.Updatedat = now;

            await _context.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            _logger.LogInformation(
                "FinalizeBookingEarly: booking {BookingId} finalized. Released {Amount} for {Completed}/{Total} completed lessons.",
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
            var completedLessonsCount = booking.Lessons!.Count(l => l.Status == Completed || l.Issettled == true);
            await _notificationService.CreateNotificationAsync(new NotificationRequest
            {
                Userid = tutorId,
                Title = "Phụ huynh ngừng học — giải ngân hoàn tất",
                Message = $"Booking #{bookingId}: Phụ huynh không thanh toán các buổi còn lại. " +
                          $"Bạn đã nhận thanh toán cho {completedLessonsCount} buổi đã dạy.",
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
