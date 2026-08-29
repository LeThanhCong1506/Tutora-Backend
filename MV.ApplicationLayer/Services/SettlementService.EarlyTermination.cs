using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.Helpers;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.Entities;
using static MV.DomainLayer.Constants.ClassSessionStatus;

namespace MV.ApplicationLayer.Services;

public partial class SettlementService
{
    /// <summary>
    /// Parent/linked-student explicitly ends a course after at least one delivered session and
    /// before paying the remaining phase. Unlike <see cref="FinalizeBookingEarlyAsync"/> (background
    /// timeout job, no caller identity), this validates ownership, row-locks the booking, and is
    /// safe to call repeatedly (double-click, or a race with the timeout job / a late webhook).
    /// Delivered sessions are NOT refunded — the tutor keeps what was already escrowed for them.
    /// Returns false (no-op, no exception) for any invalid/already-resolved state so the controller
    /// can surface a plain "can't do this right now, refresh" response instead of a 500.
    /// </summary>
    public async Task<bool> FinalizeBookingEarlyByUserAsync(
        int bookingId,
        string userId,
        string? reason = null,
        CancellationToken ct = default)
    {
        var now = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;

        await using var tx = await _context.Database.BeginTransactionAsync(ct);
        try
        {
            var booking = await _bookingRepository.FindWithRelationsForUpdateAsync(bookingId, ct);
            if (booking == null)
            {
                await tx.CommitAsync(ct);
                return false;
            }

            // Only the payer side may trigger this — never the tutor, since it releases money TO
            // the tutor. Matches the [Authorize(Roles = ParentOrStudent)] gate on the controller.
            var isOwner = booking.Parentid == userId
                || booking.Studentid == userId
                || booking.Student?.Linkeduserid == userId;
            if (!isOwner)
            {
                await tx.CommitAsync(ct);
                return false;
            }

            // Idempotent no-op: already finalized (by an earlier click, the timeout job, or the
            // normal remaining-payment flow) — nothing left to do, and never move money twice.
            if (booking.Status != BookingStatus.PendingRemainingPayment || booking.Remainingpaidat != null)
            {
                await tx.CommitAsync(ct);
                return false;
            }

            var sessions = booking.ClassSessions;
            var hasDeliveredSession = sessions.Any(s => s.Status == Completed || s.Issettled == true);
            var hasBlockingSession = sessions.Any(s =>
                s.Status is InProgress or PendingConfirmation or Disputed or NoShow or CancelledNoshow or Interrupted);

            // Nothing delivered yet → this is ordinary pre-start cancellation, not early termination.
            // Anything mid-flight (in progress, awaiting confirm, disputed, no-show, interrupted) must
            // be resolved through its own flow first — finalizing now would silently strand or
            // misprice it (an Interrupted session counts as neither delivered nor remaining below, so
            // its escrow would otherwise never be released once this method sets Status=Completed).
            if (!hasDeliveredSession || hasBlockingSession)
            {
                await tx.CommitAsync(ct);
                return false;
            }

            if (string.IsNullOrWhiteSpace(booking.Tutorid))
            {
                await tx.CommitAsync(ct);
                return false;
            }

            var tutorWallet = await WalletLockHelper.GetRequiredForUpdateAsync(_context, booking.Tutorid, ct);

            // Deterministic target computed from immutable booking pricing, never from the wallet's
            // current balance: NET-per-session (Tutorfee/Totalsessions) × sessions actually delivered.
            var deliveredCount = sessions.Count(s => s.Status == Completed || s.Issettled == true);
            var perSession = LessonRefundCalculator.TutorEscrowPerSession(booking);
            var target = Math.Round(perSession * deliveredCount, 2);

            // Idempotency against partial prior releases for THIS booking (e.g. a per-session dispute
            // refund already released one session's escrow) — release only what's still outstanding.
            var alreadyReleased = await _context.Wallettransactions
                .Where(t => t.Walletid == tutorWallet.Walletid
                            && t.Referencetable == ReferenceTable.Booking
                            && t.Referenceid == bookingId
                            && t.Transactiontype == TransactionType.EscrowRelease)
                .SumAsync(t => t.Amount ?? 0, ct);

            var releaseAmount = Math.Min(Math.Max(0, target - alreadyReleased), tutorWallet.Frozenbalance ?? 0);

            if (releaseAmount > 0)
            {
                tutorWallet.Frozenbalance = Math.Max(0, (tutorWallet.Frozenbalance ?? 0) - releaseAmount);
                tutorWallet.Balance = (tutorWallet.Balance ?? 0) + releaseAmount;
                tutorWallet.Lastupdated = now;

                var reasonSuffix = string.IsNullOrWhiteSpace(reason) ? "" : $" — Lý do: {reason}";
                _context.Wallettransactions.Add(new Wallettransaction
                {
                    Walletid = tutorWallet.Walletid,
                    Amount = releaseAmount,
                    Transactiontype = TransactionType.EscrowRelease,
                    Referencetable = ReferenceTable.Booking,
                    Referenceid = bookingId,
                    Description = $"Kết thúc sớm khóa học #{bookingId} — đã dạy {deliveredCount} buổi{reasonSuffix}",
                    Createdat = now
                });
            }

            // Cancel only sessions that have not been delivered yet.
            foreach (var session in sessions.Where(s => s.Status is Scheduled or Reserved))
                session.Status = Cancelled;

            // Supersede any still-active PayOS request for the remaining phase so it stops being
            // offered/payable; a late webhook for a superseded request must reconcile as an orphan
            // refund on arrival, never re-apply as if it were still the live remaining payment.
            var remainingRequests = await _context.PaymentRequests
                .Where(r => r.Bookingid == bookingId && r.Phase == PaymentRequestPhase.Remaining)
                .ToListAsync(ct);
            foreach (var request in remainingRequests.Where(r => PaymentRequestStatus.IsActive(r.Status)))
            {
                request.Status = PaymentRequestStatus.Superseded;
                request.Updatedat = now;
            }

            // Paymentstatus is deliberately left at DepositEscrowed — it already truthfully means
            // "only the deposit phase was ever escrowed"; the remaining phase genuinely never was.
            booking.Status = BookingStatus.Completed;
            booking.Escrowstatus = EscrowStatus.Released;
            booking.Sessionsremaining = 0;
            booking.Paymentdueat = null;
            booking.Updatedat = now;

            await _context.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            _logger.LogInformation(
                "User {UserId} finalized booking {BookingId} early: released {Amount} for {Delivered} delivered session(s), {Cancelled} future session(s) cancelled.",
                userId, bookingId, releaseAmount, deliveredCount, sessions.Count(s => s.Status == Cancelled));

            try
            {
                await _notificationService.CreateNotificationAsync(new NotificationRequest
                {
                    Userid = booking.Tutorid,
                    Title = "Giải ngân khóa học thành công",
                    Message = $"Khóa học #{bookingId} đã kết thúc sớm. Bạn đã nhận {releaseAmount:N0}đ cho {deliveredCount} buổi đã dạy.",
                    Type = NotificationType.SettlementReleased,
                    Referenceid = bookingId.ToString()
                });

                if (!string.IsNullOrWhiteSpace(booking.Parentid))
                {
                    await _notificationService.CreateNotificationAsync(new NotificationRequest
                    {
                        Userid = booking.Parentid,
                        Title = "Khóa học đã kết thúc",
                        Message = $"Khóa học #{bookingId} đã được kết thúc sớm theo yêu cầu của bạn. Các buổi chưa học đã được hủy.",
                        Type = NotificationType.CourseCompleted,
                        Referenceid = bookingId.ToString()
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send finalize-early notifications for booking {BookingId}", bookingId);
            }

            // Kết thúc sớm cũng cho ra status Completed nên khóa này vẫn đánh giá được.
            await NotifyBookingCompletedAsync(booking);

            return true;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    /// <summary>
    /// Admin/staff hủy booking sau khi xác minh NGOÀI hệ thống (qua tổng đài) rằng phụ huynh đã
    /// "nghỉ ngang" — không còn tham gia/phản hồi. Không có luồng dispute nào gắn với hành động
    /// này — gia sư báo cáo hoàn toàn ngoài hệ thống (gọi tổng đài), staff xác minh và thao tác
    /// trực tiếp trên CMS.
    /// Tiền chia theo mặc định công bằng: buổi ĐÃ dạy được giải ngân ngay cho gia sư (Frozenbalance
    /// → Balance); buổi CHƯA dạy hoàn lại cho phụ huynh theo giá gốc (không gồm 5% phí dịch vụ) —
    /// dùng chung công thức với "Hủy khóa học & hoàn tiền" của case dispute qua <see cref="CancelRemainingSessionsAsync"/>.
    /// Trả về false (no-op, không exception) cho mọi trạng thái không hợp lệ để controller trả lỗi
    /// chung, không 500.
    /// </summary>
    public async Task<bool> CancelGhostBookingAsync(
        int bookingId,
        string adminId,
        string? reason = null,
        CancellationToken ct = default)
    {
        await using var tx = await _context.Database.BeginTransactionAsync(ct);
        try
        {
            // Lock booking trước — CancelRemainingSessionsAsync giả định caller đã lock.
            var booking = await _bookingRepository.FindWithRelationsForUpdateAsync(bookingId, ct);
            if (booking == null)
            {
                await tx.CommitAsync(ct);
                return false;
            }

            if (DisputeSettlementPolicy.IsTerminalBooking(booking.Status)
                || string.IsNullOrWhiteSpace(booking.Tutorid))
            {
                await tx.CommitAsync(ct);
                return false;
            }

            // Buổi khác đang mid-flight phải xử lý xong qua đúng luồng của nó trước — cùng lý do
            // với FinalizeBookingEarlyByUserAsync. Pre-check ở đây (đã có ClassSessions load sẵn
            // từ FindWithRelationsForUpdateAsync) để trả false chính xác thay vì dựa vào no-op
            // im lặng bên trong CancelRemainingSessionsAsync.
            var hasBlockingSession = booking.ClassSessions.Any(s =>
                s.Status is InProgress or PendingConfirmation or Disputed or NoShow or Interrupted);
            if (hasBlockingSession)
            {
                await tx.CommitAsync(ct);
                return false;
            }

            var refundedToParent = await CancelRemainingSessionsAsync(
                bookingId, adminId, BookingStatus.CancelledByStaff, reason, ct);

            await tx.CommitAsync(ct);

            _logger.LogInformation(
                "Admin {AdminId} cancelled booking {BookingId} as parent-ghost: refunded {Amount} to parent for undelivered sessions.",
                adminId, bookingId, refundedToParent);

            return true;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }
}
