using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.Helpers;
using System.Data;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Entities;
using MV.DomainLayer.Exceptions;
using MV.DomainLayer.Helpers;
using static MV.DomainLayer.Constants.PaymentStatus;

namespace MV.ApplicationLayer.Services;

public partial class PaymentService
{
    // ─── Wallet Payment ───────────────────────────────────────────────────

    public Task PayWithWalletAsync(
        int bookingId,
        string userId,
        CancellationToken ct = default)
        => PayWithWalletCoreAsync(bookingId, userId, null, ct);

    private async Task PayWithWalletCoreAsync(
        int bookingId,
        string userId,
        string? expectedPhase,
        CancellationToken ct)
    {
        Booking booking;
        bool isDepositPhase;

        await using (var tx = await context.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable,
            ct))
        {
            try
            {
                booking = await bookingRepo
                    .FindWithRelationsForUpdateAsync(bookingId, ct)
                    ?? throw new BookingException(
                        BookingErrorCodes.BookingNotFound,
                        ApiMessages.BookingNotFound,
                        404);
                var ownsBooking = booking.Parentid == userId
                    || booking.Studentid == userId
                    || booking.Student?.Linkeduserid == userId;
                if (!ownsBooking)
                {
                    throw new BookingException(
                        BookingErrorCodes.BookingNotFound,
                        ApiMessages.BookingNotFound,
                        404);
                }

                EnsureDepositAmountsCalculated(booking);

                isDepositPhase = booking.Depositpaidat == null;

                var currentPhase = isDepositPhase
                    ? PaymentRequestPhase.Deposit
                    : PaymentRequestPhase.Remaining;
                if (expectedPhase != null && expectedPhase != currentPhase)
                {
                    throw new BookingException(
                        BookingErrorCodes.BookingAlreadyPaid,
                        "Giai đoạn thanh toán của booking đã thay đổi. Tiền nạp bù vẫn được giữ an toàn trong ví.",
                        409);
                }

        if (isDepositPhase)
        {
            if (booking.Status != BookingStatus.Accepted && booking.Status != BookingStatus.PendingPayment)
                throw new BookingException(BookingErrorCodes.InvalidBookingStatus, "Booking not ready for payment", 409);
            if (booking.Paymentdueat <= MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow)
                throw new BookingException(BookingErrorCodes.BookingExpired, "Booking payment expired", 409);
        }
        else
        {
            if (booking.Remainingpaidat != null)
                throw new BookingException(BookingErrorCodes.RemainingAlreadyPaid, "Phần thanh toán còn lại đã được thanh toán rồi", 409);
            if (booking.Status != BookingStatus.DepositPaid && booking.Status != BookingStatus.PendingRemainingPayment
                && booking.Status != BookingStatus.Ongoing)
                throw new BookingException(BookingErrorCodes.InvalidBookingStatus, "Booking not ready for remaining payment", 409);
            // Chặn thanh toán phần còn lại khi đã quá hạn (đồng bộ với luồng PayOS ConfirmRemainingAsync).
            // Tránh "hồi sinh" booking mà hệ thống đã/đang finalize-early và race với PaymentTimeoutJob.
            if (booking.Paymentdueat <= MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow)
                throw new BookingException(BookingErrorCodes.BookingExpired, "Đã quá hạn thanh toán phần còn lại", 409);
        }

        if (booking.Paymentstatus == Escrowed || booking.Status == BookingStatus.Paid)
            throw new BookingException(BookingErrorCodes.BookingAlreadyPaid, "Booking already paid", 409);

        var amount = isDepositPhase
            ? (int)(booking.Depositamount ?? 0)
            : (int)(booking.Remainingamount ?? 0);

        if (amount <= 0)
            throw new BookingException(BookingErrorCodes.InvalidInput, "Số tiền booking không hợp lệ", 400);

        // Học sinh tự đăng ký (không có Parentid) trả từ ngưỡng LargeTransactionPolicy trở lên
        // phải xác thực OTP gửi tới SĐT phụ huynh trước — xem BookingController.SendPaymentOtp/
        // VerifyPaymentOtp. Phụ huynh (booking có Parentid) không bao giờ đi qua chặn này.
        if (string.IsNullOrWhiteSpace(booking.Parentid) && amount >= LargeTransactionPolicy.ThresholdAmount)
        {
            var phase = isDepositPhase ? PaymentPhase.Deposit : PaymentPhase.Remaining;
            var approved = await largeTransactionOtpService.IsApprovedAsync(bookingId, phase);
            if (!approved)
                throw new LargeTransactionOtpRequiredException(phase);
        }

        var txType = isDepositPhase ? TransactionType.DepositPayment : TransactionType.RemainingPayment;
        var description = isDepositPhase
            ? $"Deposit for booking #{bookingId}"
            : $"Remaining payment for booking #{bookingId}";

            var wallet = await walletRepo.GetByUserIdForUpdateAsync(userId, ct)
                ?? throw new BookingException(BookingErrorCodes.BookingNotFound, "Không tìm thấy ví người dùng", 404);

            if ((wallet.Balance ?? 0) < amount)
                throw new BookingException(WalletErrorCodes.InsufficientBalance, "Số dư ví không đủ, vui lòng nạp thêm tiền", 400);

            wallet.Balance = (wallet.Balance ?? 0) - amount;
            wallet.Lastupdated = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;

            walletRepo.AddTransaction(new Wallettransaction
            {
                Wallet = wallet,
                Amount = -amount,
                Transactiontype = txType,
                Referencetable = ReferenceTable.Booking,
                Referenceid = bookingId,
                Description = description,
                Createdat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow
            });

            var txId = $"wallet-{bookingId}-{(isDepositPhase ? PaymentPhase.DepositShort : PaymentPhase.RemainingShort)}-{MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow:yyyyMMddHHmmss}";

            context.PaymentTransactions.Add(
                WalletPaymentTransactionFactory.Create(
                    bookingId,
                    userId,
                    isDepositPhase,
                    amount,
                    MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow));

            if (isDepositPhase)
            {
                booking.Status = BookingStatus.PendingTutor;
                booking.Paymentstatus = DepositEscrowed;
                booking.Depositpaidat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
                booking.Paymentdueat = null;
                // Cùng luật với nhánh thanh toán cổng ngoài — xem BookingLeadTimePolicy.
                booking.Responsedeadline = BookingLeadTimePolicy.ResolveResponseDeadline(
                    MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow,
                    booking.ClassSessions.OrderBy(x => x.Scheduledstart).Select(x => (DateTime?)x.Scheduledstart).FirstOrDefault());
                booking.Escrowstatus = EscrowStatus.Holding;
                booking.Updatedat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;

                if (!string.IsNullOrWhiteSpace(booking.Tutorid))
                {
                    var tutorWallet = await walletRepo.GetOrCreateForUpdateAsync(booking.Tutorid, ct);
                    var totalEscrow = booking.Tutorfee ?? 0;
                    var walletSessions = booking.Totalsessions ?? 1;
                    var depositEscrow = Math.Round(totalEscrow / walletSessions, 2);
                    tutorWallet.Frozenbalance = (tutorWallet.Frozenbalance ?? 0) + depositEscrow;
                    tutorWallet.Lastupdated = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;

                    walletRepo.AddTransaction(new Wallettransaction
                    {
                        Wallet = tutorWallet,
                        Amount = depositEscrow,
                        Transactiontype = TransactionType.EscrowCredit,
                        Referencetable = ReferenceTable.Payment,
                        Referenceid = bookingId,
                        Description = txId,
                        Createdat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow
                    });
                }

                // Single-session booking: the full amount is already escrowed, but tutor still must accept.
                if ((booking.Remainingamount ?? 0) <= 0)
                {
                    booking.Paymentstatus = Escrowed;
                    booking.Remainingpaidat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
                }

            }
            else
            {
                // Đã trả đủ (qua ví): còn buổi chưa hoàn tất → "ongoing"; hết buổi → "paid"
                // (SettlementService đưa về completed khi Sessionsremaining = 0). Đồng bộ với ConfirmRemainingAsync.
                booking.Status = (booking.Sessionsremaining ?? 0) > 0
                    ? BookingStatus.Ongoing
                    : BookingStatus.Paid;
                booking.Paymentstatus = Escrowed;
                booking.Remainingpaidat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
                booking.Paymentdueat = null;
                booking.Updatedat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;

                if (!string.IsNullOrWhiteSpace(booking.Tutorid))
                {
                    var tutorWallet = await walletRepo.GetOrCreateForUpdateAsync(booking.Tutorid, ct);
                    var totalEscrow = booking.Tutorfee ?? 0;
                    var walletRemSessions = booking.Totalsessions ?? 1;
                    var depositEscrow = Math.Round(totalEscrow / walletRemSessions, 2);
                    var remainingEscrow = totalEscrow - depositEscrow;
                    tutorWallet.Frozenbalance = (tutorWallet.Frozenbalance ?? 0) + remainingEscrow;
                    tutorWallet.Lastupdated = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;

                    walletRepo.AddTransaction(new Wallettransaction
                    {
                        Wallet = tutorWallet,
                        Amount = remainingEscrow,
                        Transactiontype = TransactionType.EscrowCredit,
                        Referencetable = ReferenceTable.Payment,
                        Referenceid = bookingId,
                        Description = txId,
                        Createdat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow
                    });
                }

                // Remaining amount is now paid → activate sessions 2..N (were reserved until now).
                await ActivateRemainingSessionsAsync(bookingId, ct);
            }

            await context.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }
        }

        // The wallet transaction is disposed before PayOS cleanup starts. This
        // allows the cleanup helper to open its own serializable transaction and
        // prevents post-commit notification failures from attempting rollback.
        await SupersedeActivePayOSRequestsAsync(
            bookingId,
            isDepositPhase
                ? PaymentRequestPhase.Deposit
                : PaymentRequestPhase.Remaining,
            "Booking phase was paid with wallet balance.",
            ct);

        await SendPaymentPhaseNotificationsAsync(booking, isDepositPhase);

        logger.LogInformation("Parent {ParentId} paid {Phase} for booking {BookingId} with wallet",
            userId, isDepositPhase ? PaymentPhase.Deposit : PaymentPhase.Remaining, bookingId);
    }

    public async Task<WalletSummaryResponse> GetTutorWalletSummaryAsync(string tutorId)
    {
        var wallet = await walletRepo.GetByUserIdAsNoTrackingAsync(tutorId);
        var balance = wallet?.Balance ?? 0;
        var frozenBalance = wallet?.Frozenbalance ?? 0;

        return new WalletSummaryResponse
        {
            Balance = balance,
            AvailableBalance = balance,
            FrozenBalance = frozenBalance,
            TotalBalance = balance + frozenBalance,
            LastUpdated = wallet != null ? wallet.Lastupdated : null
        };
    }

    private async Task SendPaymentPhaseNotificationsAsync(Booking booking, bool isDepositPhase)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(booking.Parentid))
            {
                await notificationService.CreateNotificationAsync(new NotificationRequest
                {
                    Userid = booking.Parentid,
                    Title = isDepositPhase ? "Thanh toán buổi đầu tiên thành công" : "Thanh toán hoàn tất",
                    Message = isDepositPhase
                        ? $"Đã thanh toán buổi học đầu tiên ({booking.Depositamount:N0}đ) cho booking #{booking.Bookingid}. Booking đang chờ gia sư xác nhận."
                        : $"Đã thanh toán các buổi học còn lại ({booking.Remainingamount:N0}đ) cho booking #{booking.Bookingid}. Booking đã được thanh toán đầy đủ.",
                    Type = NotificationType.PaymentSuccess,
                    Referenceid = booking.Bookingid.ToString()
                });
            }

            if (!string.IsNullOrWhiteSpace(booking.Tutorid))
            {
                await notificationService.CreateNotificationAsync(new NotificationRequest
                {
                    Userid = booking.Tutorid,
                    Title = isDepositPhase ? "Yêu cầu đặt lịch mới đã thanh toán" : "Booking đã thanh toán đầy đủ",
                    // Hạn phản hồi KHÔNG còn cố định 24 giờ (BookingLeadTimePolicy chặn trên bằng
                    // giờ học), nên phải ghi mốc thật. Ghi "24 giờ" như trước sẽ khiến gia sư ngủ
                    // một giấc rồi mất booking mà không hiểu vì sao.
                    Message = isDepositPhase
                        ? $"Phụ huynh đã thanh toán buổi học đầu tiên cho booking #{booking.Bookingid}. "
                          + $"Vui lòng phản hồi trước {booking.Responsedeadline:HH:mm dd/MM/yyyy}."
                        : $"Booking #{booking.Bookingid} đã được thanh toán đầy đủ.",
                    Type = isDepositPhase ? NotificationType.BookingNew : NotificationType.PaymentSuccess,
                    Referenceid = booking.Bookingid.ToString()
                });
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Không thể gửi thông báo thanh toán cho booking {BookingId}", booking.Bookingid);
        }
    }

    /// <summary>
    /// Activates the remaining sessions of a booking once the parent has paid the remaining amount.
    /// Sessions 2..N were created up-front at booking time in <c>reserved</c> state (invisible to
    /// lists/calendar/stats); this flips them to <c>scheduled</c> and assigns their Agora meet link.
    /// The first session was already activated on tutor acceptance. Runs inside the caller's
    /// transaction — it tracks + mutates but does NOT SaveChanges/Commit.
    /// </summary>
    private async Task ActivateRemainingSessionsAsync(int bookingId, CancellationToken ct)
    {
        var reserved = await context.ClassSessions
            .Where(l => l.Bookingid == bookingId && l.Status == ClassSessionStatus.Reserved)
            .OrderBy(l => l.Scheduledstart)
            .ToListAsync(ct);

        if (reserved.Count == 0) return;

        var now = TimeZoneHelper.UtcNow;
        var shiftedCount = 0;

        foreach (var classSession in reserved)
        {
            // Lưới an toàn: RemainingPaymentDeadlinePolicy đã chặn hạn 48h không vượt quá giờ học
            // buổi reserved gần nhất, nhưng vẫn có thể có trường hợp hoạt động này chạy trễ (job
            // hangfire kẹt, admin can thiệp tay...) khiến Scheduledstart đã trôi vào quá khứ. Nếu
            // cứ Scheduled với giờ cũ, buổi đó coi như đã mất — không ai từng vào phòng đúng giờ đã
            // đặt. Tự dời sang cùng giờ, +7 ngày mỗi vòng cho tới khi ở tương lai, thay vì kích hoạt
            // mù với giờ đã qua.
            if (classSession.Scheduledstart <= now)
            {
                var (newStart, newEnd) = PastDueSessionShiftPolicy.ShiftIntoFuture(
                    classSession.Scheduledstart, classSession.Scheduledend, now);
                classSession.Scheduledstart = newStart;
                classSession.Scheduledend = newEnd;
                shiftedCount++;
            }

            classSession.Status = ClassSessionStatus.Scheduled;
            // Agora RTC: channel = classSessionId (deterministic), same convention as first session.
            if (string.IsNullOrWhiteSpace(classSession.Meetinglink))
                classSession.Meetinglink = classSession.Classsessionid.ToString();
        }

        if (shiftedCount > 0)
        {
            logger.LogWarning(
                "Booking {BookingId}: {Count} session(s) đã quá giờ học khi kích hoạt (thanh toán về trễ) — đã tự dời +7 ngày mỗi vòng.",
                bookingId, shiftedCount);
        }

        logger.LogInformation("Activated {Count} remaining sessions for booking {BookingId} after remaining payment",
            reserved.Count, bookingId);
    }
}
