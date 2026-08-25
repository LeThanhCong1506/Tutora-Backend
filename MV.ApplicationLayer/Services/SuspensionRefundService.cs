using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.Helpers;
using MV.ApplicationLayer.Interfaces;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Entities;
using MV.DomainLayer.Helpers;
using static MV.DomainLayer.Constants.ClassSessionStatus;

namespace MV.ApplicationLayer.Services;

/// <inheritdoc cref="ISuspensionRefundService"/>
public class SuspensionRefundService : ISuspensionRefundService
{
    private readonly IAppDbContext _context;
    private readonly INotificationService _notificationService;
    private readonly ILogger<SuspensionRefundService> _logger;

    /// <summary>Courses money has been collected for and sessions can still be scheduled on.
    /// Anything outside this list is already terminal or not yet paid, so there is nothing to unwind.</summary>
    private static readonly string[] LiveBookingStatuses =
    {
        BookingStatus.Paid,
        BookingStatus.Ongoing,
        BookingStatus.DepositPaid,
        BookingStatus.PendingRemainingPayment,
        BookingStatus.Accepted
    };

    /// <summary>Sessions that belong to another flow (settlement, dispute, no-show, interruption).
    /// The cascade neither cancels nor prices them, and leaves the booking open so that flow can finish.</summary>
    private static readonly string[] BlockingSessionStatuses =
    {
        InProgress, PendingConfirmation, Disputed, NoShow, CancelledNoshow, Interrupted
    };

    public SuspensionRefundService(
        IAppDbContext context,
        INotificationService notificationService,
        ILogger<SuspensionRefundService> logger)
    {
        _context = context;
        _notificationService = notificationService;
        _logger = logger;
    }

    public async Task<SuspensionRefundImpactResponse> CascadeSuspensionAsync(
        string tutorId,
        DateTime? suspensionEndDate,
        string reason,
        CancellationToken ct = default)
    {
        var impact = new SuspensionRefundImpactResponse();
        if (string.IsNullOrWhiteSpace(tutorId)) return impact;

        // Callers reach us from three places: an admin suspending directly (no transaction yet),
        // the warning threshold firing inside CreateSuspensionAsync, and the no-show/dispute flows
        // which already hold a Serializable transaction with the booking and wallets locked.
        // Joining the ambient transaction keeps the whole thing one atomic unit; opening a nested
        // one would deadlock against the locks the outer flow already took.
        var ownsTx = _context.Database.CurrentTransaction is null;
        await using var tx = ownsTx
            ? await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct)
            : null;

        var pendingNotifications = impact.PendingNotifications;

        try
        {
            // The outer flow may have unflushed session/wallet edits, and the clamp queries below
            // read from the database. Flush first so we never refund against a stale picture.
            if (!ownsTx) await _context.SaveChangesAsync(ct);

            var bookingIds = await _context.Bookings
                .AsNoTracking()
                .Where(b => b.Tutorid == tutorId && LiveBookingStatuses.Contains(b.Status!))
                .Select(b => b.Bookingid)
                .ToListAsync(ct);

            var now = TimeZoneHelper.UtcNow;

            foreach (var bookingId in bookingIds)
            {
                var bookingImpact = await CascadeOneBookingAsync(
                    bookingId, tutorId, suspensionEndDate, reason, now, impact, pendingNotifications, ct);

                if (bookingImpact == null) continue;

                impact.Bookings.Add(bookingImpact);
                impact.BookingsAffected++;
                impact.SessionsCancelled += bookingImpact.SessionsCancelled;
                impact.TotalRefunded += bookingImpact.RefundAmount;
                impact.TotalEscrowReversed += bookingImpact.EscrowReversed;
                if (bookingImpact.Closed) impact.BookingsClosed++;
            }

            await _context.SaveChangesAsync(ct);
            if (tx is not null) await tx.CommitAsync(ct);
        }
        catch
        {
            if (tx is not null) await tx.RollbackAsync(ct);
            throw;
        }

        _logger.LogInformation(
            "Suspension cascade for tutor {TutorId} (until {EndDate}): {Bookings} booking(s), {Sessions} session(s) cancelled, {Refunded} refunded, {Reversed} escrow reversed, {Released} released to tutor.",
            tutorId, suspensionEndDate?.ToString("O") ?? "indefinite", impact.BookingsAffected,
            impact.SessionsCancelled, impact.TotalRefunded, impact.TotalEscrowReversed,
            impact.TotalEscrowReleasedToTutor);

        // Only announce once the money is actually committed. When nested, the outer flow owns the
        // commit and could still roll back, so the payloads go back to that caller to send after
        // its own commit — see SuspensionRefundImpactResponse.PendingNotifications.
        if (ownsTx)
        {
            await NotifyImpactAsync(impact);
        }
        else if (pendingNotifications.Count > 0)
        {
            impact.NotificationsDeferred = true;

            // Whoever opened the ambient transaction must call NotifyImpactAsync after committing.
            // A caller that drops the impact (the warning threshold tripping deep inside a no-show
            // or dispute settlement) still moves the money correctly — the payer sees it in their
            // wallet history — but never gets a push about it, so make that traceable.
            _logger.LogWarning(
                "Suspension cascade for tutor {TutorId} deferred {Count} notification(s) to the ambient transaction owner; they are dropped if that caller does not flush them.",
                tutorId, pendingNotifications.Count);
        }

        return impact;
    }

    public async Task NotifyImpactAsync(SuspensionRefundImpactResponse impact)
    {
        if (impact.PendingNotifications.Count == 0) return;

        // Take the batch before sending so a caller that retries cannot double-notify.
        var batch = impact.PendingNotifications.ToList();
        impact.PendingNotifications.Clear();
        impact.NotificationsDeferred = false;

        foreach (var notification in batch)
        {
            try
            {
                await _notificationService.CreateNotificationAsync(notification);
            }
            catch (Exception ex)
            {
                // A failed notification must never undo a committed refund.
                _logger.LogWarning(ex,
                    "Failed to send suspension-cascade notification to {UserId} for booking {BookingId}",
                    notification.Userid, notification.Referenceid);
            }
        }
    }

    /// <summary>
    /// Unwinds one booking. Returns null when the booking has nothing the cascade should touch.
    /// </summary>
    private async Task<SuspensionRefundBookingImpact?> CascadeOneBookingAsync(
        int bookingId,
        string tutorId,
        DateTime? suspensionEndDate,
        string reason,
        DateTime now,
        SuspensionRefundImpactResponse impact,
        List<NotificationRequest> pendingNotifications,
        CancellationToken ct)
    {
        // Same lock order the no-show and settlement flows use: booking -> sessions -> wallets.
        var booking = await _context.Bookings
            .FromSqlRaw(SqlQueries.LockBookingById, bookingId)
            .SingleOrDefaultAsync(ct);
        if (booking == null) return null;

        // Re-check under the lock: another flow may have finished this booking while we queued.
        if (!LiveBookingStatuses.Contains(booking.Status!)) return null;

        var sessions = await _context.ClassSessions
            .Where(s => s.Bookingid == bookingId)
            .ToListAsync(ct);

        // A permanent suspension or an indefinite block cancels everything undelivered; a
        // temporary one only reaches sessions that fall inside the window the tutor is away.
        var affected = sessions
            .Where(s => s.Status is Scheduled or Reserved)
            .Where(s => suspensionEndDate == null || s.Scheduledstart <= suspensionEndDate.Value)
            .ToList();

        if (affected.Count == 0) return null;

        var refundRecipientId = await ResolvePayerUserIdAsync(booking, ct);

        // Every path that creates a student profile fills in Linkeduserid, so this is unreachable
        // on sound data. It stays as a guard because the alternative — cancelling sessions we have
        // no wallet to pay back into — would silently swallow the payer's money.
        if (string.IsNullOrWhiteSpace(refundRecipientId))
        {
            _logger.LogError(
                "Suspension cascade skipped booking {BookingId} for tutor {TutorId}: no payer account could be resolved from Parentid or the student profile.",
                bookingId, tutorId);
            impact.BookingsNeedingManualReview.Add(bookingId);
            return null;
        }

        // Get-or-create rather than require: a legacy tutor with no wallet row must not make the
        // account impossible to suspend. An empty wallet simply reverses no escrow, and the payer
        // is still refunded out of what was actually collected.
        var tutorWallet = await WalletLockHelper.GetOrCreateForUpdateAsync(_context, tutorId, now, ct);
        var payerWallet = await WalletLockHelper.GetOrCreateForUpdateAsync(_context, refundRecipientId, now, ct);

        // Never refund more than was actually collected. Only the deposit is in hand until the
        // remaining phase is paid, and earlier dispute/no-show refunds already gave some of it back.
        var totalPaidByParent = booking.Remainingpaidat.HasValue
            ? (booking.Finalprice ?? 0)
            : (booking.Depositpaidat.HasValue ? (booking.Depositamount ?? 0) : 0m);
        var totalAlreadyRefunded = await _context.Wallettransactions
            .Where(t => t.Referencetable == ReferenceTable.Booking
                        && t.Referenceid == bookingId
                        && t.Transactiontype == TransactionType.Refund)
            .SumAsync(t => t.Amount ?? 0, ct);
        var maxParentRefund = Math.Max(0, totalPaidByParent - totalAlreadyRefunded);

        var refundAmount = Math.Round(
            Math.Min(LessonRefundCalculator.ParentRefundPerSession(booking) * affected.Count, maxParentRefund), 2);
        var escrowReversal = Math.Round(
            Math.Min(LessonRefundCalculator.TutorEscrowPerSession(booking) * affected.Count,
                     Math.Max(0, tutorWallet.Frozenbalance ?? 0)), 2);

        var sessionLabel = suspensionEndDate == null ? "gia sư bị khóa" : "gia sư bị tạm đình chỉ";

        if (escrowReversal > 0)
        {
            tutorWallet.Frozenbalance = Math.Max(0, (tutorWallet.Frozenbalance ?? 0) - escrowReversal);
            tutorWallet.Lastupdated = now;
            _context.Wallettransactions.Add(new Wallettransaction
            {
                Walletid = tutorWallet.Walletid,
                Amount = -escrowReversal,
                Transactiontype = TransactionType.EscrowReversal,
                Referencetable = ReferenceTable.Booking,
                Referenceid = bookingId,
                Description = $"Hoàn escrow do {sessionLabel} — khóa học #{bookingId} ({affected.Count} buổi chưa dạy)",
                Createdat = now
            });
        }

        if (refundAmount > 0)
        {
            payerWallet.Balance = (payerWallet.Balance ?? 0) + refundAmount;
            payerWallet.Lastupdated = now;
            _context.Wallettransactions.Add(new Wallettransaction
            {
                Walletid = payerWallet.Walletid,
                Amount = refundAmount,
                Transactiontype = TransactionType.Refund,
                Referencetable = ReferenceTable.Booking,
                Referenceid = bookingId,
                Description = $"Hoàn tiền do {sessionLabel} — khóa học #{bookingId} ({affected.Count} buổi chưa dạy)",
                Createdat = now
            });
        }

        foreach (var session in affected)
            session.Status = Cancelled;

        booking.Sessionsremaining = Math.Max(0, (booking.Sessionsremaining ?? affected.Count) - affected.Count);
        booking.Refundamount = (booking.Refundamount ?? 0) + refundAmount;
        booking.Refundstatus = RefundStatus.Refunded;
        booking.Updatedat = now;

        var result = new SuspensionRefundBookingImpact
        {
            BookingId = bookingId,
            RefundRecipientId = refundRecipientId,
            SessionsCancelled = affected.Count,
            RefundAmount = refundAmount,
            EscrowReversed = escrowReversal
        };

        // A session mid-settlement, in dispute, or awaiting the payer's confirmation still owes the
        // booking an outcome. Leave the course open — ReleaseEscrowIfBookingCompleteAsync closes it
        // when that last session resolves — rather than pricing it here from the outside.
        var hasBlockingSession = sessions.Any(s => BlockingSessionStatuses.Contains(s.Status!));
        var hasTeachableSessionLeft = sessions.Any(s => s.Status is Scheduled or Reserved);

        if (!hasBlockingSession && !hasTeachableSessionLeft)
            await CloseBookingAsync(booking, sessions, tutorWallet, now, result, impact, ct);
        else
            result.BookingStatus = booking.Status;

        pendingNotifications.Add(new NotificationRequest
        {
            Userid = refundRecipientId,
            Title = result.Closed ? "Khóa học đã dừng — đã hoàn tiền" : "Buổi học bị hủy — đã hoàn tiền",
            Message = BuildPayerMessage(bookingId, affected.Count, refundAmount, suspensionEndDate, result.Closed, reason),
            Type = NotificationType.PaymentRefundSuccess,
            Referenceid = bookingId.ToString()
        });

        pendingNotifications.Add(new NotificationRequest
        {
            Userid = tutorId,
            Title = result.Closed ? "Khóa học đã bị dừng" : "Buổi học đã bị hủy",
            Message = $"Do tài khoản của bạn bị {(suspensionEndDate == null ? "khóa" : "tạm đình chỉ")}, "
                    + $"{affected.Count} buổi chưa dạy của khóa học #{bookingId} đã bị hủy và {refundAmount:N0}đ đã được hoàn cho người học.",
            Type = NotificationType.BookingCancelled,
            Referenceid = bookingId.ToString()
        });

        // When a parent booked on a child's behalf, the child is the one who would have shown up.
        // They get told the sessions are gone; the money talk goes to the parent who paid.
        var studentUserId = await GetStudentAccountIdAsync(booking, ct);
        if (!string.IsNullOrWhiteSpace(studentUserId) && studentUserId != refundRecipientId)
        {
            pendingNotifications.Add(new NotificationRequest
            {
                Userid = studentUserId,
                Title = result.Closed ? "Khóa học đã dừng" : "Buổi học sắp tới đã bị hủy",
                Message = $"Gia sư của khóa học #{bookingId} hiện không thể tiếp tục giảng dạy nên "
                        + $"{affected.Count} buổi học sắp tới đã bị hủy. "
                        + (result.Closed
                            ? "Khóa học đã kết thúc, phụ huynh của bạn đã được hoàn tiền."
                            : "Các buổi học sau đó vẫn được giữ nguyên."),
                Type = NotificationType.BookingCancelled,
                Referenceid = bookingId.ToString()
            });
        }

        return result;
    }

    /// <summary>
    /// The account of the student who attends this course, if they have one.
    /// </summary>
    private async Task<string?> GetStudentAccountIdAsync(Booking booking, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(booking.Studentid)) return null;

        return await _context.Studentprofiles
            .AsNoTracking()
            .Where(s => s.Studentid == booking.Studentid)
            .Select(s => s.Linkeduserid)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// Who gets the money back: the account that paid for the course.
    /// </summary>
    /// <remarks>
    /// A parent booking for their child pays from the parent wallet. A student who signed up
    /// themselves pays from their own. Booking.Studentid is the student *profile* key, which is a
    /// user id only for self-registered students — parent-created children get a generated one —
    /// so it is used last and only after confirming a matching account exists, never as a blind
    /// fallback that would break the wallet's foreign key.
    /// </remarks>
    private async Task<string?> ResolvePayerUserIdAsync(Booking booking, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(booking.Parentid)) return booking.Parentid;
        if (string.IsNullOrWhiteSpace(booking.Studentid)) return null;

        // The booking came from a raw locking query, so its Student navigation is not loaded.
        var linkedUserId = await _context.Studentprofiles
            .AsNoTracking()
            .Where(s => s.Studentid == booking.Studentid)
            .Select(s => s.Linkeduserid)
            .FirstOrDefaultAsync(ct);

        if (!string.IsNullOrWhiteSpace(linkedUserId)) return linkedUserId;

        // Older self-registered students can predate Linkeduserid being filled in, and for them the
        // profile key *is* the account id — accept it once the account is confirmed to exist.
        var studentAccountExists = await _context.Users
            .AsNoTracking()
            .AnyAsync(u => u.Userid == booking.Studentid, ct);

        return studentAccountExists ? booking.Studentid : null;
    }

    /// <summary>
    /// Nothing teachable is left, so the course ends here. Sessions already delivered are still the
    /// tutor's to keep — their escrow is released the same way a normally-completed booking would.
    /// </summary>
    private async Task CloseBookingAsync(
        Booking booking,
        List<ClassSession> sessions,
        Wallet tutorWallet,
        DateTime now,
        SuspensionRefundBookingImpact result,
        SuspensionRefundImpactResponse impact,
        CancellationToken ct)
    {
        var deliveredCount = sessions.Count(s => s.Status == Completed || s.Issettled == true);

        if (deliveredCount > 0)
        {
            // Escrow in this platform is only released when a course finishes, so the tutor's
            // delivered sessions are still sitting frozen. Pay them out before closing the booking.
            var target = Math.Round(LessonRefundCalculator.TutorEscrowPerSession(booking) * deliveredCount, 2);
            var alreadyReleased = await _context.Wallettransactions
                .Where(t => t.Walletid == tutorWallet.Walletid
                            && t.Referencetable == ReferenceTable.Booking
                            && t.Referenceid == booking.Bookingid
                            && t.Transactiontype == TransactionType.EscrowRelease)
                .SumAsync(t => t.Amount ?? 0, ct);

            var release = Math.Min(Math.Max(0, target - alreadyReleased), tutorWallet.Frozenbalance ?? 0);
            if (release > 0)
            {
                tutorWallet.Frozenbalance = Math.Max(0, (tutorWallet.Frozenbalance ?? 0) - release);
                tutorWallet.Balance = (tutorWallet.Balance ?? 0) + release;
                tutorWallet.Lastupdated = now;
                _context.Wallettransactions.Add(new Wallettransaction
                {
                    Walletid = tutorWallet.Walletid,
                    Amount = release,
                    Transactiontype = TransactionType.EscrowRelease,
                    Referencetable = ReferenceTable.Booking,
                    Referenceid = booking.Bookingid,
                    Description = $"Giải ngân {deliveredCount} buổi đã dạy — khóa học #{booking.Bookingid} dừng do gia sư bị đình chỉ",
                    Createdat = now
                });
                impact.TotalEscrowReleasedToTutor += release;
            }

            booking.Status = BookingStatus.Completed;
            booking.Escrowstatus = EscrowStatus.Released;
        }
        else
        {
            // The tutor never delivered anything, so this is a plain cancellation — give the
            // promotion use back the way every other pre-delivery termination does.
            booking.Status = BookingStatus.Cancelled;
            booking.Escrowstatus = EscrowStatus.Refunded;
            booking.Cancelledat = now;
            booking.Cancelledby = SystemActors.SystemUpper;
            booking.Cancellationreason = "Gia sư bị đình chỉ";
            await PromotionUsageHelper.ReturnUsageAsync(_context, booking.Promotionid, ct);
        }

        booking.Sessionsremaining = 0;
        booking.Paymentdueat = null;

        // Stop offering the remaining-phase payment link for a course that no longer exists. A late
        // webhook for a superseded request reconciles as an orphan refund instead of re-activating it.
        var remainingRequests = await _context.PaymentRequests
            .Where(r => r.Bookingid == booking.Bookingid && r.Phase == PaymentRequestPhase.Remaining)
            .ToListAsync(ct);
        foreach (var request in remainingRequests.Where(r => PaymentRequestStatus.IsActive(r.Status)))
        {
            request.Status = PaymentRequestStatus.Superseded;
            request.Updatedat = now;
        }

        result.Closed = true;
        result.BookingStatus = booking.Status;
    }

    private static string BuildPayerMessage(
        int bookingId, int cancelledCount, decimal refundAmount, DateTime? endDate, bool closed, string reason)
    {
        var cause = endDate == null
            ? "Gia sư của khóa học này đã bị khóa tài khoản"
            : $"Gia sư của khóa học này đang bị tạm đình chỉ đến {FormatVietnamDate(endDate.Value)}";

        var outcome = closed
            ? $"Khóa học #{bookingId} đã được dừng lại"
            : $"{cancelledCount} buổi học sắp tới của khóa #{bookingId} đã được hủy";

        return $"{cause}. {outcome} và {refundAmount:N0}đ đã được hoàn vào ví của bạn. "
             + (closed
                ? "Bạn có thể đặt lịch với gia sư khác bất cứ lúc nào."
                : "Các buổi học sau thời gian này vẫn được giữ nguyên.");
    }

    /// <summary>Timestamps are stored in UTC; notifications must read in the user's local time.</summary>
    private static string FormatVietnamDate(DateTime utc)
    {
        var vietnamTimeZone = TimeZoneHelper.GetTimeZoneInfo("Asia/Ho_Chi_Minh");
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), vietnamTimeZone);
        return local.ToString("dd/MM/yyyy");
    }

    public async Task<SuspensionRefundImpactResponse> PreviewCascadeAsync(
        string tutorId,
        DateTime? suspensionEndDate,
        CancellationToken ct = default)
    {
        var impact = new SuspensionRefundImpactResponse();
        if (string.IsNullOrWhiteSpace(tutorId)) return impact;

        var bookings = await _context.Bookings
            .AsNoTracking()

            .Include(b => b.ClassSessions)
            .Where(b => b.Tutorid == tutorId && LiveBookingStatuses.Contains(b.Status!))
            .ToListAsync(ct);

        var frozenLeft = await _context.Wallets
            .AsNoTracking()
            .Where(w => w.Userid == tutorId)
            .Select(w => w.Frozenbalance ?? 0)
            .FirstOrDefaultAsync(ct);

        foreach (var booking in bookings)
        {
            var affected = booking.ClassSessions
                .Where(s => s.Status is Scheduled or Reserved)
                .Where(s => suspensionEndDate == null || s.Scheduledstart <= suspensionEndDate.Value)
                .ToList();

            if (affected.Count == 0) continue;

            // Same resolution order the cascade uses, so the preview can never promise a refund
            // the real run would skip (or vice versa).
            var refundRecipientId = await ResolvePayerUserIdAsync(booking, ct);

            if (string.IsNullOrWhiteSpace(refundRecipientId))
            {
                impact.BookingsNeedingManualReview.Add(booking.Bookingid);
                continue;
            }

            var totalPaidByParent = booking.Remainingpaidat.HasValue
                ? (booking.Finalprice ?? 0)
                : (booking.Depositpaidat.HasValue ? (booking.Depositamount ?? 0) : 0m);
            var totalAlreadyRefunded = await _context.Wallettransactions
                .Where(t => t.Referencetable == ReferenceTable.Booking
                            && t.Referenceid == booking.Bookingid
                            && t.Transactiontype == TransactionType.Refund)
                .SumAsync(t => t.Amount ?? 0, ct);

            var refundAmount = Math.Round(Math.Min(
                LessonRefundCalculator.ParentRefundPerSession(booking) * affected.Count,
                Math.Max(0, totalPaidByParent - totalAlreadyRefunded)), 2);

            // Escrow is one shared frozen pot across every booking, so preview it the way the
            // cascade spends it: booking by booking, against what the previous ones left behind.
            var escrowReversal = Math.Round(Math.Min(
                LessonRefundCalculator.TutorEscrowPerSession(booking) * affected.Count,
                Math.Max(0, frozenLeft)), 2);
            frozenLeft -= escrowReversal;

            var hasBlockingSession = booking.ClassSessions.Any(s => BlockingSessionStatuses.Contains(s.Status!));
            var closed = !hasBlockingSession
                && !booking.ClassSessions.Any(s => s.Status is Scheduled or Reserved && !affected.Contains(s));

            impact.Bookings.Add(new SuspensionRefundBookingImpact
            {
                BookingId = booking.Bookingid,
                RefundRecipientId = refundRecipientId,
                SessionsCancelled = affected.Count,
                RefundAmount = refundAmount,
                EscrowReversed = escrowReversal,
                Closed = closed,
                BookingStatus = closed
                    ? (booking.ClassSessions.Any(s => s.Status == Completed || s.Issettled == true)
                        ? BookingStatus.Completed
                        : BookingStatus.Cancelled)
                    : booking.Status
            });

            impact.BookingsAffected++;
            impact.SessionsCancelled += affected.Count;
            impact.TotalRefunded += refundAmount;
            impact.TotalEscrowReversed += escrowReversal;
            if (closed) impact.BookingsClosed++;
        }

        return impact;
    }
}
