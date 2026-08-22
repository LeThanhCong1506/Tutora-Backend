using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.Helpers;
using MV.ApplicationLayer.Interfaces;
using MV.ApplicationLayer.RepositoryInterfaces;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Entities;
using MV.DomainLayer.Exceptions;
using MV.DomainLayer.Helpers;
using static MV.DomainLayer.Constants.ClassSessionStatus;
namespace MV.ApplicationLayer.Services;

/// <summary>
/// Service for settlement and escrow management
/// </summary>
public partial class SettlementService : ISettlementService
{
    private readonly IAppDbContext _context;
    private readonly IBookingRepository _bookingRepository;
    private readonly INotificationService _notificationService;
    private readonly ILogger<SettlementService> _logger;

    public SettlementService(
        IAppDbContext context,
        IBookingRepository bookingRepository,
        INotificationService notificationService,
        ILogger<SettlementService> logger)
    {
        _context = context;
        _bookingRepository = bookingRepository;
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

    /// <summary>
    /// Settle a classSession as part of admin dispute resolution (Release / side-with-tutor).
    /// Unlike <see cref="SettleClassSessionAsync"/> this does NOT require PendingConfirmation/Completed status —
    /// a Disputed or NoShow classSession is allowed (admin override). Still refuses an already-settled classSession.
    /// </summary>
    public async Task<SettlementResultResponse> SettleDisputedClassSessionAsync(int classSessionId, string? confirmedBy = null)
    {
        var classSession = await _context.ClassSessions
            .Include(l => l.Booking)
            .FirstOrDefaultAsync(l => l.Classsessionid == classSessionId)
            ?? throw new ClassSessionException(ClassSessionErrorCodes.ClassSessionNotFound, "Không tìm thấy buổi học", 404);

        if (classSession.Issettled == true)
            throw new ClassSessionException(ClassSessionErrorCodes.ClassSessionAlreadyConfirmed, "Buổi học này đã được xử lý rồi", 400);

        return await SettleClassSessionInternalAsync(classSession, confirmedBy, SettlementType.Manual);
    }

    private async Task<SettlementResultResponse> SettleClassSessionInternalAsync(ClassSession classSession, string? confirmedBy, string settlementType)
    {
        // Reuse an ambient transaction if the caller already opened one (e.g. dispute resolution),
        // otherwise own a fresh Serializable transaction. Prevents nested-BeginTransaction failures.
        var ownsTx = _context.Database.CurrentTransaction is null;
        await using var tx = ownsTx
            ? await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable)
            : null;
        try
        {
            var tutorId = classSession.Tutorid;

            // Get tutor's wallet (row lock)
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

            // Decrement remaining sessions; release held escrow if the booking just completed.
            if (classSession.Booking != null && (classSession.Booking.Sessionsremaining ?? 0) > 0)
                classSession.Booking.Sessionsremaining -= 1;

            var amountReleasedNow = await ReleaseEscrowIfBookingCompleteAsync(classSession.Booking, tutorWallet);
            var isBookingCompleted = classSession.Booking?.Status == BookingStatus.Completed;

            await _context.SaveChangesAsync();
            if (tx is not null) await tx.CommitAsync();

            _logger.LogInformation("Settled classSession {ClassSessionId}, amount {Amount} released to tutor {TutorId}",
                classSession.Classsessionid, amountReleasedNow, tutorId);

            // Notification only when this method owns the transaction. When called inside a dispute's
            // transaction the outer caller hasn't committed yet and sends its own notifications.
            if (ownsTx)
            {
                try
                {
                    if (isBookingCompleted)
                    {
                        await _notificationService.CreateNotificationAsync(new NotificationRequest
                        {
                            Userid = tutorId,
                            Title = "Giải ngân khóa học thành công",
                            Message = $"Khóa học #{classSession.Bookingid} đã hoàn thành. Bạn đã nhận được tổng cộng {amountReleasedNow:N0}đ. Số dư ví hiện tại: {tutorWallet.Balance:N0}đ",
                            Type = NotificationType.SettlementReleased,
                            Referenceid = classSession.Bookingid?.ToString()
                        });
                    }
                    else
                    {
                        await _notificationService.CreateNotificationAsync(new NotificationRequest
                        {
                            Userid = tutorId,
                            Title = "Xác nhận buổi học",
                            Message = $"Buổi học #{classSession.Classsessionid} đã được xác nhận. Tiền học sẽ được giải ngân khi hoàn thành toàn bộ khóa học.",
                            Type = NotificationType.LessonConfirmed,
                            Referenceid = classSession.Classsessionid.ToString()
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to send settlement notification for classSession {ClassSessionId}", classSession.Classsessionid);
                }
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
                TransactionId = 0,
                NewTutorBalance = tutorWallet.Balance,
                SessionsRemaining = classSession.Booking?.Sessionsremaining
            };
        }
        catch
        {
            if (tx is not null) await tx.RollbackAsync();
            throw;
        }
    }

    /// <summary>
    /// When a booking's last session is resolved (Sessionsremaining reaches 0), release the escrow
    /// still held for delivered sessions. NET-based (Tutorfee/Totalsessions) and makeup-aware:
    /// counts classSessions in Completed status, so a settled makeup releases the original no-show's escrow.
    /// Clamped to the tutor's frozen balance so it can never go negative or release another booking's escrow.
    /// Returns the amount released (0 if the booking is not yet complete).
    /// </summary>
    private async Task<decimal> ReleaseEscrowIfBookingCompleteAsync(Booking? booking, Wallet tutorWallet)
    {
        if (booking == null || (booking.Sessionsremaining ?? 0) > 0)
            return 0;
        // Already terminal (completed elsewhere, or cancelled) → never release twice.
        if (booking.Status is BookingStatus.Completed or BookingStatus.Cancelled or BookingStatus.CancelledNoshow)
            return 0;

        booking.Status = BookingStatus.Completed;
        booking.Escrowstatus = EscrowStatus.Released;

        // Flush pending classSession status changes so the Completed count below reflects them.
        await _context.SaveChangesAsync();

        var deliveredCount = await _context.ClassSessions
            .CountAsync(l => l.Bookingid == booking.Bookingid && l.Status == Completed);
        var totalSessions = booking.Totalsessions ?? deliveredCount;
        var perSession = LessonRefundCalculator.TutorEscrowPerSession(booking);
        var target = Math.Round(perSession * Math.Min(deliveredCount, totalSessions), 2);
        var release = Math.Min(target, tutorWallet.Frozenbalance ?? 0);

        if (release > 0)
        {
            tutorWallet.Frozenbalance = Math.Max(0, (tutorWallet.Frozenbalance ?? 0) - release);
            tutorWallet.Balance = (tutorWallet.Balance ?? 0) + release;
            tutorWallet.Lastupdated = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
            _context.Wallettransactions.Add(new Wallettransaction
            {
                Walletid = tutorWallet.Walletid,
                Amount = release,
                Transactiontype = TransactionType.EscrowRelease,
                Referencetable = ReferenceTable.Booking,
                Referenceid = booking.Bookingid,
                Description = $"Giải ngân hoàn tất khóa học #{booking.Bookingid}",
                Createdat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow
            });
        }

        await NotifyBookingCompletedAsync(booking);

        return release;
    }

    /// <summary>
    /// Mời người học đánh giá khi khóa học hoàn thành. Người nhận theo đúng luật của
    /// <c>FeedbackService.CanReviewBooking</c> — booking có phụ huynh thì phụ huynh đánh giá,
    /// không thì học sinh tự đăng ký — để không nhắc nhầm người không đánh giá được.
    /// Thông báo hỏng không được làm hỏng settlement, nên nuốt lỗi và chỉ ghi log.
    /// </summary>
    private async Task NotifyBookingCompletedAsync(Booking booking)
    {
        var recipientId = booking.Parentid;

        if (string.IsNullOrEmpty(recipientId) && !string.IsNullOrEmpty(booking.Studentid))
        {
            // Booking.Studentid trỏ tới studentprofiles.student_id, KHÔNG phải user id. Với học
            // sinh tự đăng ký hai giá trị trùng nhau, nhưng hồ sơ do phụ huynh tạo thì student_id
            // là mã sinh tự động — tra qua hồ sơ để lấy đúng tài khoản đăng nhập, khớp với
            // FeedbackService.CanReviewBooking (Studentid hoặc Linkeduserid).
            recipientId = await _context.Studentprofiles
                .Where(s => s.Studentid == booking.Studentid)
                .Select(s => s.Linkeduserid ?? s.Studentid)
                .FirstOrDefaultAsync();
        }

        if (string.IsNullOrEmpty(recipientId)) return;

        try
        {
            await _notificationService.CreateNotificationAsync(new NotificationRequest
            {
                Userid = recipientId,
                Title = "Khóa học đã hoàn thành",
                Message = "Khóa học đã kết thúc. Hãy dành một phút đánh giá gia sư để giúp phụ huynh khác lựa chọn.",
                Type = NotificationType.FeedbackRequest,
                Referenceid = booking.Bookingid.ToString()
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send feedback request notification for booking {BookingId}",
                booking.Bookingid);
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
                .ThenInclude(b => b!.Student)
            .FirstOrDefaultAsync(l => l.Classsessionid == classSessionId)
            ?? throw new ClassSessionException(ClassSessionErrorCodes.ClassSessionNotFound, "Không tìm thấy buổi học", 404);

        if (classSession.Issettled == true)
            throw new ClassSessionException(ClassSessionErrorCodes.ClassSessionAlreadyConfirmed, "Buổi học này đã được xác nhận rồi", 400);

        var ownsTx = _context.Database.CurrentTransaction is null;
        await using var tx = ownsTx
            ? await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable)
            : null;
        try
        {
            var booking = classSession.Booking
                ?? throw new ClassSessionException(ClassSessionErrorCodes.ClassSessionNotFound, "Buổi học không có thông tin booking để tính hoàn tiền", 400);
            var parentRefundPerSession = LessonRefundCalculator.ParentRefundPerSession(booking);
            var tutorEscrowPerSession = LessonRefundCalculator.TutorEscrowPerSession(booking);
            // Người nhận hoàn tiền = người đã trả: phụ huynh đặt hộ → Parentid; học sinh tự đặt → ví học sinh.
            var refundRecipientId = !string.IsNullOrWhiteSpace(booking.Parentid)
                ? booking.Parentid
                : (!string.IsNullOrWhiteSpace(booking.Student?.Linkeduserid)
                    ? booking.Student!.Linkeduserid
                    : booking.Studentid);
            var tutorId = classSession.Tutorid;

            // Get wallets (with row lock for concurrency safety)
            var tutorWallet = await _context.Wallets
                .FromSqlRaw(SqlQueries.LockWalletByUserId, tutorId)
                .FirstOrDefaultAsync();

            // Tự tạo ví nếu người nhận chưa có (học sinh tự đặt thường đã có ví sau xác minh tuổi, nhưng phòng hờ).
            var parentWallet = !string.IsNullOrWhiteSpace(refundRecipientId)
                ? await WalletLockHelper.GetOrCreateForUpdateAsync(_context, refundRecipientId, MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow)
                : null;

            // Clamp against what's actually collected/frozen — LessonRefundCalculator's per-session
            // math assumes the full booking (both payment phases) is collected, which isn't true if
            // "remaining" hasn't been paid yet (only the deposit — exactly session 1's share — has).
            // Same clamping pattern as ProcessNoShowActionAsync's ChangeTutor branch.
            var totalPaidByParent = booking.Remainingpaidat.HasValue
                ? (booking.Finalprice ?? 0)
                : (booking.Depositpaidat.HasValue ? (booking.Depositamount ?? 0) : 0m);
            var totalAlreadyRefunded = await _context.Wallettransactions
                .Where(wt => wt.Referencetable == ReferenceTable.Booking
                          && wt.Referenceid == classSession.Bookingid
                          && wt.Transactiontype == TransactionType.Refund)
                .SumAsync(wt => wt.Amount ?? 0);
            var maxParentRefund = Math.Max(0, totalPaidByParent - totalAlreadyRefunded);

            if (tutorWallet != null)
                tutorEscrowPerSession = Math.Round(Math.Min(tutorEscrowPerSession, Math.Max(0, tutorWallet.Frozenbalance ?? 0)), 2);

            var refundAmount = Math.Round(Math.Min(parentRefundPerSession * refundPercentage / 100m, maxParentRefund), 2);
            var tutorAmount = Math.Round(tutorEscrowPerSession * (100 - refundPercentage) / 100m, 2);

            // Release exactly one session's worth of escrow from tutor frozen balance
            if (tutorWallet != null)
            {
                tutorWallet.Frozenbalance = Math.Max(0, (tutorWallet.Frozenbalance ?? 0) - tutorEscrowPerSession);
                if (tutorAmount > 0)
                    tutorWallet.Balance += tutorAmount;
                tutorWallet.Lastupdated = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;

                // Ghi ledger cho phía tutor (trước đây thiếu → totalEarned báo thiếu & sổ cái không khớp):
                //  - Phần tutor thực nhận cho buổi đã dạy → EscrowRelease (tính vào thu nhập).
                if (tutorAmount > 0)
                    _context.Wallettransactions.Add(new Wallettransaction
                    {
                        Walletid = tutorWallet.Walletid,
                        Amount = tutorAmount,
                        Transactiontype = TransactionType.EscrowRelease,
                        Referencetable = ReferenceTable.Booking,
                        Referenceid = classSession.Bookingid,
                        Description = $"Giải ngân buổi học #{classSession.Classsessionid} ({100 - refundPercentage}%)",
                        Createdat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow
                    });

                //  - Phần escrow bị rút để hoàn cho phụ huynh → EscrowReversal (không phải thu nhập).
                var tutorReversed = Math.Round(tutorEscrowPerSession - tutorAmount, 2);
                if (tutorReversed > 0)
                    _context.Wallettransactions.Add(new Wallettransaction
                    {
                        Walletid = tutorWallet.Walletid,
                        Amount = -tutorReversed,
                        Transactiontype = TransactionType.EscrowReversal,
                        Referencetable = ReferenceTable.Booking,
                        Referenceid = classSession.Bookingid,
                        Description = $"Hoàn escrow buổi học #{classSession.Classsessionid} ({refundPercentage}%)",
                        Createdat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow
                    });
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
                    Referenceid = classSession.Bookingid,
                    Description = $"Hoàn tiền buổi học #{classSession.Classsessionid} ({refundPercentage}%)",
                    Createdat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow
                });
            }

            // Update classSession: cancelled on a full refund, completed (delivered but disputed) otherwise.
            bool isClassSessionCancelled = refundPercentage == 100;
            classSession.Status = isClassSessionCancelled ? Cancelled : Completed;
            classSession.Issettled = true;

            // The session is resolved either way → remove it from the booking's countdown
            // (previously a 100% refund INCREMENTED Sessionsremaining, which prevented the booking
            // from ever completing and left the remaining escrow frozen forever).
            if ((booking.Sessionsremaining ?? 0) > 0)
                booking.Sessionsremaining -= 1;

            // If this refund is the last unresolved session, release the escrow still held for
            // the sessions already delivered.
            if (tutorWallet != null)
                await ReleaseEscrowIfBookingCompleteAsync(booking, tutorWallet);

            await _context.SaveChangesAsync();
            if (tx is not null) await tx.CommitAsync();

            // Notify parent only when this method owns the tx (the dispute caller sends its own).
            if (ownsTx)
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(refundRecipientId) && refundAmount > 0)
                    {
                        await _notificationService.CreateNotificationAsync(new NotificationRequest
                        {
                            Userid = refundRecipientId,
                            Title = "Hoàn tiền buổi học",
                            Message = $"Bạn đã nhận được hoàn tiền {refundAmount:N0}đ ({refundPercentage}%) cho buổi học #{classSession.Classsessionid}. Số dư ví: {parentWallet?.Balance:N0}đ",
                            Type = NotificationType.PaymentRefundSuccess,
                            Referenceid = classSession.Classsessionid.ToString()
                        });
                    }
                }
                catch (Exception ex) { _logger.LogWarning(ex, "Failed to send refund notification for classSession {ClassSessionId}", classSession.Classsessionid); }
            }

            var settlementType = refundPercentage switch
            {
                100 => SettlementType.FullRefund,
                50 => SettlementType.Refund50,
                _ => $"refund_{refundPercentage}"
            };

            _logger.LogInformation("Processed refund for classSession {ClassSessionId}: {RefundPercent}% (parent refund {RefundAmount}, tutor earns {TutorAmount})",
                classSession.Classsessionid, refundPercentage, refundAmount, tutorAmount);

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
            if (tx is not null) await tx.RollbackAsync();
            throw;
        }
    }

    // Múi giờ Việt Nam (UTC+7) – dùng cho response hiển thị (now using VietnamTimeHelper)

    public async Task<RefundPreviewResponse> PreviewRefundAsync(int classSessionId, int refundPercentage)
    {
        if (refundPercentage < 0 || refundPercentage > 100)
            throw new ArgumentException("Phần trăm hoàn tiền phải nằm trong khoảng từ 0 đến 100");

        var classSession = await _context.ClassSessions
            .AsNoTracking()
            .Include(l => l.Booking)
            .FirstOrDefaultAsync(l => l.Classsessionid == classSessionId)
            ?? throw new ClassSessionException(ClassSessionErrorCodes.ClassSessionNotFound, "Không tìm thấy buổi học", 404);

        var booking = classSession.Booking
            ?? throw new ClassSessionException(ClassSessionErrorCodes.ClassSessionNotFound, "Buổi học không có thông tin booking để tính hoàn tiền", 400);

        var parentRefundPerSession = LessonRefundCalculator.ParentRefundPerSession(booking);
        var tutorEscrowPerSession = LessonRefundCalculator.TutorEscrowPerSession(booking);

        var tutorWallet = await _context.Wallets.AsNoTracking().FirstOrDefaultAsync(w => w.Userid == classSession.Tutorid);
        var tutorFrozenBalance = tutorWallet?.Frozenbalance ?? 0;

        var totalPaidByParent = booking.Remainingpaidat.HasValue
            ? (booking.Finalprice ?? 0)
            : (booking.Depositpaidat.HasValue ? (booking.Depositamount ?? 0) : 0m);
        var totalAlreadyRefunded = await _context.Wallettransactions
            .Where(wt => wt.Referencetable == ReferenceTable.Booking
                      && wt.Referenceid == classSession.Bookingid
                      && wt.Transactiontype == TransactionType.Refund)
            .SumAsync(wt => wt.Amount ?? 0);
        var maxParentRefund = Math.Max(0, totalPaidByParent - totalAlreadyRefunded);

        var clampedTutorEscrow = Math.Round(Math.Min(tutorEscrowPerSession, Math.Max(0, tutorFrozenBalance)), 2);
        var rawRefundAmount = Math.Round(parentRefundPerSession * refundPercentage / 100m, 2);
        var refundAmount = Math.Round(Math.Min(rawRefundAmount, maxParentRefund), 2);
        var tutorAmount = Math.Round(clampedTutorEscrow * (100 - refundPercentage) / 100m, 2);

        var warnings = new List<string>();
        if (refundAmount < rawRefundAmount)
            warnings.Add(
                $"Số tiền hoàn phụ huynh bị giới hạn còn {refundAmount:N0}đ (thay vì {rawRefundAmount:N0}đ) vì mới thu được {totalPaidByParent:N0}đ cho booking này" +
                (totalAlreadyRefunded > 0 ? $" (đã hoàn {totalAlreadyRefunded:N0}đ trước đó)." : "."));
        if (clampedTutorEscrow < tutorEscrowPerSession)
            warnings.Add($"Escrow gia sư không đủ — chỉ giải phóng được {clampedTutorEscrow:N0}đ thay vì {tutorEscrowPerSession:N0}đ (số dư đóng băng hiện tại: {tutorFrozenBalance:N0}đ).");

        return new RefundPreviewResponse
        {
            ClassSessionId = classSessionId,
            Percentage = refundPercentage,
            ParentRefundAmount = refundAmount,
            TutorPayoutAmount = tutorAmount,
            IsDepositPaid = booking.Depositpaidat.HasValue,
            IsRemainingPaid = booking.Remainingpaidat.HasValue,
            TutorFrozenBalance = tutorFrozenBalance,
            Warnings = warnings
        };
    }

    /// <summary>
    /// Hủy toàn bộ các buổi CHƯA diễn ra (Scheduled/Reserved) còn lại của một booking và hoàn cho
    /// phụ huynh theo GIÁ GỐC mỗi buổi (<see cref="LessonRefundCalculator.ParentRefundPerSessionNoFee"/>
    /// — không gồm 5% phí dịch vụ, khác mọi luồng refund khác trong hệ thống). Buổi ĐÃ hoàn thành
    /// (Completed/Issettled) không bị hủy, và phần escrow của các buổi đó được GIẢI NGÂN NGAY cho
    /// gia sư (chuyển Frozenbalance → Balance) thay vì tiếp tục kẹt lại chờ Sessionsremaining=0 —
    /// điều kiện đó sẽ không bao giờ xảy ra nữa vì booking đang bị hủy giữa chừng.
    /// Dùng chung cho 2 luồng: (1) admin giải quyết tranh chấp bằng "Hủy khóa học & hoàn tiền"
    /// (<paramref name="bookingStatus"/> = <see cref="BookingStatus.CancelledByDispute"/> — caller
    /// tự settle buổi đang khiếu nại riêng TRƯỚC khi gọi hàm này, nên nó không còn ở
    /// Scheduled/Reserved và tự động không bị đụng tới); (2) staff hủy booking do phụ huynh "nghỉ
    /// ngang" (<paramref name="bookingStatus"/> = <see cref="BookingStatus.CancelledByStaff"/>).
    /// PHẢI được gọi bên trong một transaction đang mở, với booking đã được lock (FOR UPDATE) bởi
    /// caller — hàm này không tự mở transaction hay tự lock booking.
    /// </summary>
    public async Task<decimal> CancelRemainingSessionsAsync(
        int bookingId, string processedBy, string bookingStatus, string? reason, CancellationToken ct = default)
    {
        var booking = await _context.Bookings
            .Include(b => b.Student)
            .FirstOrDefaultAsync(b => b.Bookingid == bookingId, ct)
            ?? throw new InvalidOperationException($"Booking {bookingId} not found");

        // Đã đóng bởi luồng khác (vd resolve dispute khác gần như đồng thời) — không xử lý lặp.
        if (DisputeSettlementPolicy.IsTerminalBooking(booking.Status))
            return 0m;

        var now = TimeZoneHelper.UtcNow;

        var remainingSessions = await _context.ClassSessions
            .Where(s => s.Bookingid == bookingId && (s.Status == Scheduled || s.Status == Reserved))
            .ToListAsync(ct);
        var remainingCount = remainingSessions.Count;

        // Có buổi khác đang mid-flight (không phải buổi caller vừa tự settle riêng, nếu có) — phải
        // được xử lý xong qua đúng luồng của nó trước, không hủy đè lên để tránh strand/mispricing.
        var hasBlockingSession = await _context.ClassSessions
            .AnyAsync(s => s.Bookingid == bookingId
                && (s.Status == InProgress || s.Status == PendingConfirmation || s.Status == Disputed || s.Status == NoShow),
                ct);
        if (hasBlockingSession)
            return 0m;

        var refundRecipientId = !string.IsNullOrWhiteSpace(booking.Parentid)
            ? booking.Parentid
            : (!string.IsNullOrWhiteSpace(booking.Student?.Linkeduserid)
                ? booking.Student!.Linkeduserid
                : booking.Studentid);

        var parentRefundPerSession = LessonRefundCalculator.ParentRefundPerSessionNoFee(booking);
        var tutorEscrowPerSession = LessonRefundCalculator.TutorEscrowPerSession(booking);

        // Clamp theo số tiền phụ huynh thực đã trả trừ đã hoàn trước đó — cùng pattern chống hoàn
        // vượt mức đã dùng ở ProcessRefundAsync.
        var totalPaidByParent = booking.Remainingpaidat.HasValue
            ? (booking.Finalprice ?? 0)
            : (booking.Depositpaidat.HasValue ? (booking.Depositamount ?? 0) : 0m);
        var totalAlreadyRefunded = await _context.Wallettransactions
            .Where(wt => wt.Referencetable == ReferenceTable.Booking
                      && wt.Referenceid == bookingId
                      && wt.Transactiontype == TransactionType.Refund)
            .SumAsync(wt => wt.Amount ?? 0, ct);
        var maxParentRefund = Math.Max(0, totalPaidByParent - totalAlreadyRefunded);

        var parentTotalRefund = Math.Round(Math.Min(remainingCount * parentRefundPerSession, maxParentRefund), 2);

        var tutorWallet = !string.IsNullOrWhiteSpace(booking.Tutorid)
            ? await _context.Wallets.FromSqlRaw(SqlQueries.LockWalletByUserId, booking.Tutorid).FirstOrDefaultAsync(ct)
            : null;

        // Mô tả ngắn cho ledger/notification tùy theo nguồn gọi — không đổi công thức tiền, chỉ
        // đổi câu chữ cho đúng ngữ cảnh (tranh chấp vs staff hủy do phụ huynh nghỉ ngang).
        var isStaffGhostCancel = bookingStatus == BookingStatus.CancelledByStaff;
        var contextLabel = isStaffGhostCancel ? "staff hủy do phụ huynh nghỉ ngang" : "giải quyết tranh chấp";

        // Giải ngân cho các buổi ĐÃ dạy trước khi hủy — nếu không làm bước này, tiền của những buổi
        // đó sẽ kẹt vĩnh viễn trong Frozenbalance (chỉ Scheduled/Reserved bị đụng ở bước reverse bên
        // dưới, còn Sessionsremaining chỉ tự release khi CẢ booking hoàn tất — mà booking này đang bị
        // hủy giữa chừng nên không bao giờ đạt điều kiện đó). Cùng công thức idempotent với
        // FinalizeBookingEarlyByUserAsync: target = đơn giá × số buổi đã dạy, trừ đi phần đã release
        // trước đó (vd 1 buổi đã được release riêng qua giải quyết tranh chấp từng buổi) để không
        // giải ngân trùng.
        var deliveredCount = await _context.ClassSessions
            .CountAsync(s => s.Bookingid == bookingId && (s.Status == Completed || s.Issettled == true), ct);

        var tutorEscrowRelease = 0m;
        if (tutorWallet != null && deliveredCount > 0)
        {
            var releaseTarget = Math.Round(tutorEscrowPerSession * deliveredCount, 2);
            var alreadyReleased = await _context.Wallettransactions
                .Where(t => t.Walletid == tutorWallet.Walletid
                            && t.Referencetable == ReferenceTable.Booking
                            && t.Referenceid == bookingId
                            && t.Transactiontype == TransactionType.EscrowRelease)
                .SumAsync(t => t.Amount ?? 0, ct);
            tutorEscrowRelease = Math.Round(
                Math.Min(Math.Max(0, releaseTarget - alreadyReleased), Math.Max(0, tutorWallet.Frozenbalance ?? 0)), 2);
        }

        if (tutorWallet != null && tutorEscrowRelease > 0)
        {
            tutorWallet.Frozenbalance = Math.Max(0, (tutorWallet.Frozenbalance ?? 0) - tutorEscrowRelease);
            tutorWallet.Balance = (tutorWallet.Balance ?? 0) + tutorEscrowRelease;
            tutorWallet.Lastupdated = now;
            _context.Wallettransactions.Add(new Wallettransaction
            {
                Walletid = tutorWallet.Walletid,
                Amount = tutorEscrowRelease,
                Transactiontype = TransactionType.EscrowRelease,
                Referencetable = ReferenceTable.Booking,
                Referenceid = bookingId,
                Description = $"Hủy khóa học #{bookingId} ({contextLabel}) — giải ngân {deliveredCount} buổi đã dạy",
                Createdat = now
            });
        }

        var tutorEscrowReverse = tutorWallet != null
            ? Math.Round(Math.Min(remainingCount * tutorEscrowPerSession, Math.Max(0, tutorWallet.Frozenbalance ?? 0)), 2)
            : 0m;

        if (tutorWallet != null && tutorEscrowReverse > 0)
        {
            tutorWallet.Frozenbalance = Math.Max(0, (tutorWallet.Frozenbalance ?? 0) - tutorEscrowReverse);
            tutorWallet.Lastupdated = now;
            _context.Wallettransactions.Add(new Wallettransaction
            {
                Walletid = tutorWallet.Walletid,
                Amount = -tutorEscrowReverse,
                Transactiontype = TransactionType.EscrowReversal,
                Referencetable = ReferenceTable.Booking,
                Referenceid = bookingId,
                Description = $"Hủy khóa học #{bookingId} ({contextLabel}, {remainingCount} buổi còn lại)",
                Createdat = now
            });
        }

        var parentWallet = !string.IsNullOrWhiteSpace(refundRecipientId)
            ? await WalletLockHelper.GetOrCreateForUpdateAsync(_context, refundRecipientId, now, ct)
            : null;

        if (parentWallet != null && parentTotalRefund > 0)
        {
            parentWallet.Balance = (parentWallet.Balance ?? 0) + parentTotalRefund;
            parentWallet.Lastupdated = now;
            _context.Wallettransactions.Add(new Wallettransaction
            {
                Walletid = parentWallet.Walletid,
                Amount = parentTotalRefund,
                Transactiontype = TransactionType.Refund,
                Referencetable = ReferenceTable.Booking,
                Referenceid = bookingId,
                Description = $"Hoàn tiền hủy khóa học #{bookingId} ({contextLabel}, " +
                              $"{remainingCount} buổi còn lại, giá gốc không gồm phí dịch vụ)",
                Createdat = now
            });
        }

        foreach (var s in remainingSessions)
            s.Status = Cancelled;

        // Supersede mọi payment request đang active để tránh webhook trễ áp dụng nhầm lên booking
        // đã đóng (vd yêu cầu nạp bù đợt 2 đang mở).
        var activeRequests = await _context.PaymentRequests
            .Where(r => r.Bookingid == bookingId)
            .ToListAsync(ct);
        foreach (var request in activeRequests.Where(r => PaymentRequestStatus.IsActive(r.Status)))
        {
            request.Status = PaymentRequestStatus.Superseded;
            request.Updatedat = now;
        }

        booking.Status = bookingStatus;
        booking.Escrowstatus = EscrowStatus.Refunded;
        booking.Sessionsremaining = 0;
        booking.Paymentdueat = null;
        booking.Cancellationreason = reason;
        booking.Cancelledby = processedBy;
        booking.Cancelledat = now;
        booking.Updatedat = now;

        await _context.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Cancelled {Count} remaining session(s) for booking {BookingId} ({Context}) by {ProcessedBy}: refunded {Amount} to parent, released {ReleaseAmount} to tutor for {DeliveredCount} delivered session(s).",
            remainingCount, bookingId, contextLabel, processedBy, parentTotalRefund, tutorEscrowRelease, deliveredCount);

        if (!string.IsNullOrWhiteSpace(refundRecipientId) && parentTotalRefund > 0)
        {
            try
            {
                await _notificationService.CreateNotificationAsync(new NotificationRequest
                {
                    Userid = refundRecipientId,
                    Title = "Hủy khóa học & hoàn tiền",
                    Message = isStaffGhostCancel
                        ? $"Khóa học của booking #{bookingId} đã được hệ thống hủy. Bạn đã nhận hoàn {parentTotalRefund:N0}đ cho {remainingCount} buổi chưa học."
                        : $"Khóa học của booking #{bookingId} đã được hủy theo kết quả giải quyết tranh chấp. " +
                          $"Bạn đã nhận hoàn {parentTotalRefund:N0}đ cho {remainingCount} buổi chưa học.",
                    Type = NotificationType.PaymentRefundSuccess,
                    Referenceid = bookingId.ToString()
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send cancel-course refund notification for booking {BookingId}", bookingId);
            }
        }

        if (!string.IsNullOrWhiteSpace(booking.Tutorid))
        {
            try
            {
                await _notificationService.CreateNotificationAsync(new NotificationRequest
                {
                    Userid = booking.Tutorid,
                    Title = "Khóa học đã bị hủy",
                    Message = isStaffGhostCancel
                        ? (tutorEscrowRelease > 0
                            ? $"Booking #{bookingId} đã bị hủy do phụ huynh không còn phản hồi. Bạn đã nhận {tutorEscrowRelease:N0}đ cho {deliveredCount} buổi đã dạy; các buổi chưa dạy đã được hủy và hoàn cho phụ huynh."
                            : $"Booking #{bookingId} đã bị hủy do phụ huynh không còn phản hồi. Các buổi chưa dạy đã được hủy và hoàn cho phụ huynh.")
                        : (tutorEscrowRelease > 0
                            ? $"Booking #{bookingId} đã bị hủy theo kết quả giải quyết tranh chấp. Bạn đã nhận {tutorEscrowRelease:N0}đ cho {deliveredCount} buổi đã dạy; các buổi chưa dạy đã được hủy."
                            : $"Booking #{bookingId} đã bị hủy theo kết quả giải quyết tranh chấp. Các buổi chưa dạy đã được hủy."),
                    Type = NotificationType.DisputeResolved,
                    Referenceid = bookingId.ToString()
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to notify tutor for cancel-course booking {BookingId}", bookingId);
            }
        }

        return parentTotalRefund;
    }

    /// <summary>
    /// Dry-run của <see cref="CancelRemainingSessionsAsync"/> — cùng công thức, chỉ đọc
    /// (AsNoTracking), không ghi ví/booking/notification. Dùng cho màn hình admin xem trước trước
    /// khi resolve dispute bằng "Hủy khóa học & hoàn tiền".
    /// </summary>
    public async Task<CourseCancelPreviewResponse> PreviewCancelRemainingSessionsAsync(int bookingId, CancellationToken ct = default)
    {
        var booking = await _context.Bookings
            .AsNoTracking()
            .Include(b => b.Student)
            .FirstOrDefaultAsync(b => b.Bookingid == bookingId, ct)
            ?? throw new InvalidOperationException($"Booking {bookingId} not found");

        var remainingCount = await _context.ClassSessions
            .AsNoTracking()
            .CountAsync(s => s.Bookingid == bookingId && (s.Status == Scheduled || s.Status == Reserved), ct);

        var parentRefundPerSession = LessonRefundCalculator.ParentRefundPerSessionNoFee(booking);
        var tutorEscrowPerSession = LessonRefundCalculator.TutorEscrowPerSession(booking);

        var totalPaidByParent = booking.Remainingpaidat.HasValue
            ? (booking.Finalprice ?? 0)
            : (booking.Depositpaidat.HasValue ? (booking.Depositamount ?? 0) : 0m);
        var totalAlreadyRefunded = await _context.Wallettransactions
            .AsNoTracking()
            .Where(wt => wt.Referencetable == ReferenceTable.Booking
                      && wt.Referenceid == bookingId
                      && wt.Transactiontype == TransactionType.Refund)
            .SumAsync(wt => wt.Amount ?? 0, ct);
        var maxParentRefund = Math.Max(0, totalPaidByParent - totalAlreadyRefunded);

        var tutorFrozenBalance = !string.IsNullOrWhiteSpace(booking.Tutorid)
            ? (await _context.Wallets.AsNoTracking().FirstOrDefaultAsync(w => w.Userid == booking.Tutorid, ct))?.Frozenbalance ?? 0
            : 0m;

        var rawParentRefund = Math.Round(remainingCount * parentRefundPerSession, 2);
        var parentRefund = Math.Round(Math.Min(rawParentRefund, maxParentRefund), 2);

        var rawTutorEscrow = Math.Round(remainingCount * tutorEscrowPerSession, 2);
        var tutorEscrowReversed = Math.Round(Math.Min(rawTutorEscrow, Math.Max(0, tutorFrozenBalance)), 2);

        var warnings = new List<string>();
        if (parentRefund < rawParentRefund)
            warnings.Add(
                $"Số tiền hoàn phụ huynh bị giới hạn còn {parentRefund:N0}đ (thay vì {rawParentRefund:N0}đ) vì mới thu được {totalPaidByParent:N0}đ cho booking này" +
                (totalAlreadyRefunded > 0 ? $" (đã hoàn {totalAlreadyRefunded:N0}đ trước đó)." : "."));
        if (tutorEscrowReversed < rawTutorEscrow)
            warnings.Add($"Escrow gia sư không đủ — chỉ giải phóng được {tutorEscrowReversed:N0}đ thay vì {rawTutorEscrow:N0}đ (số dư đóng băng hiện tại: {tutorFrozenBalance:N0}đ).");

        return new CourseCancelPreviewResponse
        {
            BookingId = bookingId,
            RemainingSessionsCount = remainingCount,
            ParentRefundAmount = parentRefund,
            TutorEscrowReversed = tutorEscrowReversed,
            TutorFrozenBalance = tutorFrozenBalance,
            Warnings = warnings
        };
    }

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
            StudentId = l.Studentid,
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
            booking.Escrowstatus = EscrowStatus.Released;
            booking.Sessionsremaining = 0;
            booking.Updatedat = now;

            await _context.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            _logger.LogInformation(
                "FinalizeBookingEarly: booking {BookingId} finalized. Released {Amount} for {Completed}/{Total} completed classSessions.",
                bookingId, releaseAmount, completedCount, totalSessions);

            await NotifyBookingCompletedAsync(booking);
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
