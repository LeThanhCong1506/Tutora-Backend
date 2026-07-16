using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MV.ApplicationLayer.Helpers;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Configuration;
using MV.DomainLayer.Constants;
using static MV.DomainLayer.Constants.PaymentStatus;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Entities;
using MV.DomainLayer.Exceptions;
using MV.ApplicationLayer.Interfaces;
using MV.ApplicationLayer.RepositoryInterfaces;
using PayOS;

namespace MV.ApplicationLayer.Services;

public partial class PaymentService(
    IAppDbContext context,
    IBookingRepository bookingRepo,
    IWalletRepository walletRepo,
    IOptions<PaymentSettings> paymentSettings,
    [FromKeyedServices(ServiceKeys.PayOS.Checkout)] PayOSClient payOS,
    INotificationService notificationService,
    ILogger<PaymentService> logger) : IPaymentService
{
    //fix link
    private readonly PayOSClient _payOS = payOS;
    private readonly PayOSLinkFactory _linkFactory = new(
        payOS,
        paymentSettings.Value.ReturnUrl,
        paymentSettings.Value.CancelUrl);

    public async Task ProcessWebhookAsync(PaymentWebhookRequest request, CancellationToken ct = default)
    {
        if (request?.Data == null)
            throw new BookingException(BookingErrorCodes.InvalidWebhookPayload, "Dữ liệu webhook không hợp lệ", 400);

        if (request.Code != PayOSWebhookCode.SuccessCode || !request.Success)
        {
            logger.LogWarning("Non-success webhook: code={Code}", request.Code);
            return;
        }

        var data = request.Data;
        logger.LogInformation("Processing webhook orderCode: {OrderCode}, amount: {Amount}", data.OrderCode, data.Amount);
        await ConfirmPaymentInternalAsync(data.OrderCode, data.Amount, data.Reference, PaymentTransactionCapture.FromPayOSWebhook(request), ct);
    }

    public async Task<bool> VerifyWebhookSignatureAsync(string payload, string signature)
    {
        try
        {
            var webhook = System.Text.Json.JsonSerializer.Deserialize<PayOS.Models.Webhooks.Webhook>(payload);
            if (webhook == null) return false;
            return await _payOS.Webhooks.VerifyAsync(webhook) != null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Webhook signature verification failed");
            return false;
        }
    }

    public async Task ConfirmPaymentByAdminAsync(int bookingId, AdminConfirmPaymentRequest request, string? actorUserId = null, CancellationToken ct = default)
    {
        if (request == null || request.Amount <= 0)
            throw new BookingException(BookingErrorCodes.InvalidInput, "Dữ liệu đầu vào không hợp lệ", 400);

        var booking = await bookingRepo.FindTrackedAsync(bookingId, ct)
            ?? throw new BookingException(BookingErrorCodes.BookingNotFound, ApiMessages.BookingNotFound, 404);

        // Ensure deposit/remaining amounts are calculated for old bookings
        EnsureDepositAmountsCalculated(booking);

        var txId = string.IsNullOrWhiteSpace(request.TransactionId)
            ? $"admin-{bookingId}-{MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow:yyyyMMddHHmmss}"
            : request.TransactionId.Trim();

        // Auto-detect phase: if deposit not paid yet, confirm deposit; otherwise confirm remaining
        long orderCode;
        if (booking.Depositpaidat == null)
            orderCode = OrderCodeHelper.GenerateBookingOrderCode(bookingId);
        else
            orderCode = OrderCodeHelper.GenerateRemainingOrderCode(bookingId);

        var capture = PaymentTransactionCapture.FromManual(request.PaidAt, actorUserId, null, txId);
        await ConfirmPaymentInternalAsync(orderCode, (int)request.Amount, txId, capture, ct);
    }

    public async Task<TestConfirmBookingPaymentResponse> ConfirmCurrentBookingPaymentForTestAsync(
        int bookingId,
        string? transactionId = null,
        CancellationToken ct = default)
    {
        var booking = await bookingRepo.FindTrackedAsync(bookingId, ct)
            ?? throw new BookingException(BookingErrorCodes.BookingNotFound, ApiMessages.BookingNotFound, 404);

        EnsureDepositAmountsCalculated(booking);

        if (booking.Paymentstatus == Escrowed || booking.Status == BookingStatus.Paid || booking.Remainingpaidat != null)
            throw new BookingException(BookingErrorCodes.BookingAlreadyPaid, "Booking đã được thanh toán rồi", 409);

        var isDepositPhase = booking.Depositpaidat == null;
        var phase = isDepositPhase ? PaymentPhase.Deposit : PaymentPhase.Remaining;
        var amount = isDepositPhase
            ? booking.Depositamount ?? 0
            : booking.Remainingamount ?? 0;

        if (amount <= 0)
            throw new BookingException(BookingErrorCodes.BookingAlreadyPaid, "Booking đã được thanh toán rồi", 409);

        var txId = string.IsNullOrWhiteSpace(transactionId)
            ? $"test-admin-{bookingId}-{(isDepositPhase ? PaymentPhase.DepositShort : PaymentPhase.RemainingShort)}-{MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow:yyyyMMddHHmmss}"
            : transactionId.Trim();

        var orderCode = isDepositPhase
            ? OrderCodeHelper.GenerateBookingOrderCode(bookingId)
            : OrderCodeHelper.GenerateRemainingOrderCode(bookingId);

        await ConfirmPaymentInternalAsync(orderCode, (int)amount, txId, null, ct);

        return new TestConfirmBookingPaymentResponse
        {
            BookingId = bookingId,
            Phase = phase,
            Amount = amount,
            TransactionId = txId
        };
    }

    public async Task<PaymentStatusResponse> GetPaymentStatusAsync(int bookingId, string userId)
    {
        var booking = await bookingRepo.FindForPaymentByUserAsync(bookingId, userId)
            ?? throw new BookingException(BookingErrorCodes.BookingNotFound, ApiMessages.BookingNotFound, 404);

        var now = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
        bool isExpired = booking.Status == BookingStatus.PaymentTimeout
            || (booking.Paymentdueat != null && booking.Paymentdueat <= now);
        bool refundedToWallet = booking.Refundstatus == RefundStatus.Refunded && (booking.Refundamount ?? 0) > 0;
        decimal refundAmount = booking.Refundamount ?? 0;

        var baseResponse = new PaymentStatusResponse
        {
            BookingId = bookingId,
            Status = booking.Paymentstatus ?? ApiMessages.Unknown,
            Amount = (int)(booking.Finalprice ?? 0),
            // "Đã trả đủ" = trạng thái paid HOẶC đã đóng cả cọc lẫn phần còn lại (khi status là ongoing).
            IsPaid = booking.Status == BookingStatus.Paid || (booking.Depositpaidat != null && booking.Remainingpaidat != null),
            DepositAmount = booking.Depositamount ?? 0,
            RemainingAmount = booking.Remainingamount ?? 0,
            IsDepositPaid = booking.Depositpaidat != null,
            IsRemainingPaid = booking.Remainingpaidat != null,
            IsExpired = isExpired,
            RefundedToWallet = refundedToWallet,
            RefundAmount = refundAmount
        };

        if (string.IsNullOrWhiteSpace(booking.Paymentcode))
            return baseResponse;

        try
        {
            var info = await _payOS.PaymentRequests.GetAsync(booking.Paymentcode);
            var payosReportsPaid = info.Status.ToString().Equals(PayOSLinkStatus.Paid, StringComparison.OrdinalIgnoreCase);

            bool isDepositLink = OrderCodeHelper.IsBookingOrderCode(info.OrderCode);
            bool isRemainingLink = OrderCodeHelper.IsRemainingOrderCode(info.OrderCode);

            bool isThisLinkPaidInDb = false;
            if (isDepositLink)
                isThisLinkPaidInDb = booking.Depositpaidat != null;
            else if (isRemainingLink)
                isThisLinkPaidInDb = booking.Remainingpaidat != null;

            var purpose = isDepositLink
                ? PaymentTransactionPurpose.BookingDeposit
                : PaymentTransactionPurpose.BookingRemaining;
            var hasRecordedPaymentTransaction = payosReportsPaid
                && isThisLinkPaidInDb
                && await context.PaymentTransactions.AsNoTracking().AnyAsync(t =>
                    t.Channel == PaymentTransactionChannel.PayOS
                    && t.Bookingid == bookingId
                    && t.Purpose == purpose
                    && t.Ordercode == info.OrderCode);

            // Confirm when the webhook has not updated the booking yet. Also re-enter
            // the idempotent confirm path to backfill transactions for bookings that
            // were already marked paid before payment_transactions was introduced.
            if (payosReportsPaid && (!isThisLinkPaidInDb || !hasRecordedPaymentTransaction))
            {
                var capture = PaymentTransactionCapture.FromPayOSPaymentLink(info);
                if (isDepositLink)
                {
                    logger.LogInformation("PayOS reports PAID for booking {BookingId} deposit but DB not updated yet. Confirming.", bookingId);
                    try
                    {
                        var orderCode = OrderCodeHelper.GenerateBookingOrderCode(bookingId);
                        var txId = capture.GetProviderTransactionId($"payos-link-{info.Id}")!;
                        await ConfirmPaymentInternalAsync(orderCode, (int)info.Amount, txId, capture, CancellationToken.None);
                    }
                    catch (Exception confirmEx)
                    {
                        logger.LogWarning(confirmEx, "Auto-confirm deposit from polling failed for booking {BookingId}", bookingId);
                    }
                }
                else if (isRemainingLink)
                {
                    logger.LogInformation("PayOS reports PAID for booking {BookingId} remaining but DB not updated yet. Confirming.", bookingId);
                    try
                    {
                        var orderCode = OrderCodeHelper.GenerateRemainingOrderCode(bookingId);
                        var txId = capture.GetProviderTransactionId($"payos-link-{info.Id}")!;
                        await ConfirmPaymentInternalAsync(orderCode, (int)info.Amount, txId, capture, CancellationToken.None);
                    }
                    catch (Exception confirmEx)
                    {
                        logger.LogWarning(confirmEx, "Auto-confirm remaining from polling failed for booking {BookingId}", bookingId);
                    }
                }
            }

            return new PaymentStatusResponse
            {
                BookingId = bookingId,
                Status = info.Status.ToString(),
                Amount = (int)info.Amount,
                AmountPaid = (int)info.AmountPaid,
                AmountRemaining = (int)info.AmountRemaining,
                IsPaid = booking.Status == BookingStatus.Paid || (booking.Depositpaidat != null && booking.Remainingpaidat != null),
                DepositAmount = booking.Depositamount ?? 0,
                RemainingAmount = booking.Remainingamount ?? 0,
                IsDepositPaid = booking.Depositpaidat != null,
                IsRemainingPaid = booking.Remainingpaidat != null,
                IsExpired = isExpired,
                RefundedToWallet = refundedToWallet,
                RefundAmount = refundAmount
            };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to get PayOS status for booking {BookingId}", bookingId);
            return baseResponse;
        }
    }

    private async Task ConfirmPaymentInternalAsync(long orderCode, int amount, string txId, PaymentTransactionCapture? capture, CancellationToken ct)
    {
        bool isDeposit = OrderCodeHelper.IsBookingOrderCode(orderCode);
        bool isRemaining = OrderCodeHelper.IsRemainingOrderCode(orderCode);

        if (isDeposit)
            await ConfirmDepositAsync(orderCode, amount, txId, capture, ct);
        else if (isRemaining)
            await ConfirmRemainingAsync(orderCode, amount, txId, capture, ct);
        else
            throw new BookingException(BookingErrorCodes.InvalidInput, "Kiểu mã đơn hàng không hợp lệ", 400);
    }

    private async Task ConfirmDepositAsync(long orderCode, int amount, string txId, PaymentTransactionCapture? capture, CancellationToken ct)
    {
        await using var tx = await context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        try
        {
            var bookingId = OrderCodeHelper.ExtractBookingId(orderCode);
            var booking = await bookingRepo.FindTrackedAsync(bookingId, ct)
                ?? throw new BookingException(BookingErrorCodes.BookingNotFound, ApiMessages.BookingNotFound, 404);

            // Already deposit-paid or fully paid
            if (booking.Depositpaidat != null || booking.Paymentstatus == Escrowed || booking.Status == BookingStatus.Paid)
            {
                logger.LogWarning("Booking {Id} deposit already paid", bookingId);
                await AddBookingPaymentTransactionIfMissingAsync(
                    capture,
                    booking,
                    PaymentTransactionPurpose.BookingDeposit,
                    amount,
                    orderCode,
                    txId,
                    "Deposit payment webhook received after it was already processed.",
                    ct);
                if (capture != null)
                {
                    await context.SaveChangesAsync(ct);
                    await tx.CommitAsync(ct);
                }
                return;
            }

            if (booking.Paymentdueat.HasValue && booking.Paymentdueat.Value <= MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow)
            {
                await RefundOrphanPaymentToWalletAsync(booking, amount, txId, ct);
                await AddBookingPaymentTransactionIfMissingAsync(
                    capture,
                    booking,
                    PaymentTransactionPurpose.BookingDeposit,
                    amount,
                    orderCode,
                    txId,
                    "Deposit payment received after booking deadline and refunded to wallet.",
                    ct);
                await context.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                logger.LogInformation("Orphan deposit refunded to wallet for expired booking {Id}, amount {Amount}.", bookingId, amount);
                if (!string.IsNullOrWhiteSpace(booking.Parentid))
                    await notificationService.CreateNotificationAsync(new NotificationRequest
                    {
                        Userid = booking.Parentid,
                        Title = "Hoàn tiền thanh toán",
                        Message = "Mã thanh toán đã hết hạn. Số tiền đã được hoàn vào ví của bạn.",
                        Type = NotificationType.PaymentRefundSuccess,
                        Referenceid = booking.Bookingid.ToString()
                    });
                return;
            }

            // Ensure deposit amounts calculated
            EnsureDepositAmountsCalculated(booking);

            if (amount != (int)(booking.Depositamount ?? 0))
                throw new BookingException(BookingErrorCodes.AmountMismatch, "Số tiền không khớp", 409);

            if (await walletRepo.HasTransactionByDescriptionAsync(txId, ReferenceTable.Payment, ct))
            {
                logger.LogWarning("Duplicate tx {TxId} for booking {Id}", txId, bookingId);
                throw new BookingException(BookingErrorCodes.DuplicateTransaction, $"Transaction '{txId}' đã được xử lý trước đó", 409);
            }

            if (booking.Status != BookingStatus.Accepted && booking.Status != BookingStatus.PendingPayment)
                throw new BookingException(BookingErrorCodes.InvalidBookingStatus, "Booking không đũ điều kiện nhận tiền cọc", 409);

            // Parent has paid the first classSession/deposit. The booking now waits for tutor approval.
            booking.Status = BookingStatus.PendingTutor;
            booking.Paymentstatus = DepositEscrowed;
            booking.Paymentdueat = null; // Clear deposit deadline
            booking.Depositpaidat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
            booking.Responsedeadline = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow.AddHours(24);
            booking.Escrowstatus = EscrowStatus.Holding;
            booking.Updatedat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;

            // Escrow first-classSession share of tutor receivable to frozen balance
            var sessions = booking.Totalsessions ?? 1;
            if (!string.IsNullOrWhiteSpace(booking.Tutorid))
            {
                var wallet = await walletRepo.GetOrCreateForUpdateAsync(booking.Tutorid, ct);

                var totalEscrow = booking.Tutorfee ?? 0;
                var depositEscrow = Math.Round(totalEscrow / sessions, 2);
                wallet.Frozenbalance = (wallet.Frozenbalance ?? 0) + depositEscrow;
                wallet.Lastupdated = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;

                walletRepo.AddTransaction(new Wallettransaction
                {
                    Wallet = wallet,
                    Amount = depositEscrow,
                    Transactiontype = TransactionType.EscrowCredit,
                    Referencetable = ReferenceTable.Payment,
                    Referenceid = booking.Bookingid,
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

            await AddBookingPaymentTransactionIfMissingAsync(
                capture,
                booking,
                PaymentTransactionPurpose.BookingDeposit,
                amount,
                orderCode,
                txId,
                "Booking deposit payment.",
                ct);

            await context.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            logger.LogInformation("Deposit confirmed for booking {Id}, amount {Amount}", bookingId, amount);

            await SendPaymentPhaseNotificationsAsync(booking, isDepositPhase: true);

        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    private async Task ConfirmRemainingAsync(long orderCode, int amount, string txId, PaymentTransactionCapture? capture, CancellationToken ct)
    {
        await using var tx = await context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        try
        {
            var bookingId = OrderCodeHelper.ExtractBookingId(orderCode);
            var booking = await bookingRepo.FindTrackedAsync(bookingId, ct)
                ?? throw new BookingException(BookingErrorCodes.BookingNotFound, ApiMessages.BookingNotFound, 404);

            // Already fully paid
            if (booking.Remainingpaidat != null || booking.Paymentstatus == Escrowed || booking.Status == BookingStatus.Paid)
            {
                logger.LogWarning("Booking {Id} remaining already paid", bookingId);
                await AddBookingPaymentTransactionIfMissingAsync(
                    capture,
                    booking,
                    PaymentTransactionPurpose.BookingRemaining,
                    amount,
                    orderCode,
                    txId,
                    "Remaining payment webhook received after it was already processed.",
                    ct);
                if (capture != null)
                {
                    await context.SaveChangesAsync(ct);
                    await tx.CommitAsync(ct);
                }
                return;
            }

            // Deposit must be paid first
            if (booking.Depositpaidat == null)
                throw new BookingException(BookingErrorCodes.InvalidBookingStatus, "Chưa thanh toán cọc", 409);

            // Late webhook after remaining deadline expired → refund to wallet
            var isExpiredOrFinalized = (booking.Paymentdueat.HasValue && booking.Paymentdueat.Value <= MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow)
                || (booking.Status != BookingStatus.DepositPaid
                    && booking.Status != BookingStatus.PendingRemainingPayment
                    && booking.Status != BookingStatus.Ongoing);

            if (isExpiredOrFinalized)
            {
                await RefundOrphanPaymentToWalletAsync(booking, amount, txId, ct);
                await AddBookingPaymentTransactionIfMissingAsync(
                    capture,
                    booking,
                    PaymentTransactionPurpose.BookingRemaining,
                    amount,
                    orderCode,
                    txId,
                    "Remaining payment received after booking deadline and refunded to wallet.",
                    ct);
                await context.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                logger.LogInformation("Orphan remaining payment refunded to wallet for expired booking {Id}, amount {Amount}.", bookingId, amount);
                if (!string.IsNullOrWhiteSpace(booking.Parentid))
                    await notificationService.CreateNotificationAsync(new NotificationRequest
                    {
                        Userid = booking.Parentid,
                        Title = "Hoàn tiền thanh toán",
                        Message = "Mã thanh toán đã hết hạn. Số tiền đã được hoàn vào ví của bạn.",
                        Type = NotificationType.PaymentRefundSuccess,
                        Referenceid = booking.Bookingid.ToString()
                    });
                return;
            }

            if (amount != (int)(booking.Remainingamount ?? 0))
                throw new BookingException(BookingErrorCodes.AmountMismatch, "Số tiền không khớp", 409);

            if (await walletRepo.HasTransactionByDescriptionAsync(txId, ReferenceTable.Payment, ct))
            {
                logger.LogWarning("Duplicate tx {TxId} for booking {Id}", txId, bookingId);
                throw new BookingException(BookingErrorCodes.DuplicateTransaction, $"Transaction '{txId}' đã được xử lý trước đó", 409);
            }

            if (booking.Status != BookingStatus.DepositPaid && booking.Status != BookingStatus.PendingRemainingPayment
                && booking.Status != BookingStatus.Ongoing)
                throw new BookingException(BookingErrorCodes.InvalidBookingStatus, "Booking không đũ điều kiện nhận thanh toán phần còn lại", 409);

            // Update booking for fully paid.
            // Đã trả đủ: nếu còn buổi chưa hoàn tất (Sessionsremaining > 0) thì chuyển sang
            // "đang học" (ongoing); nếu đã dạy hết thì để "paid" (SettlementService sẽ đưa về
            // completed khi Sessionsremaining = 0). Tránh kẹt vĩnh viễn ở "paid" giữa lúc đang học.
            booking.Status = (booking.Sessionsremaining ?? 0) > 0
                ? BookingStatus.Ongoing
                : BookingStatus.Paid;
            booking.Paymentstatus = Escrowed;
            booking.Remainingpaidat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
            booking.Paymentdueat = null; // Clear remaining deadline
            booking.Updatedat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;

            // Escrow remaining classSessions' share of tutor receivable to frozen balance
            if (!string.IsNullOrWhiteSpace(booking.Tutorid))
            {
                var wallet = await walletRepo.GetOrCreateForUpdateAsync(booking.Tutorid, ct);

                var totalEscrow = booking.Tutorfee ?? 0;
                var remainingSessions = booking.Totalsessions ?? 1;
                var depositEscrow = Math.Round(totalEscrow / remainingSessions, 2);
                var remainingEscrow = totalEscrow - depositEscrow;
                wallet.Frozenbalance = (wallet.Frozenbalance ?? 0) + remainingEscrow;
                wallet.Lastupdated = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;

                walletRepo.AddTransaction(new Wallettransaction
                {
                    Wallet = wallet,
                    Amount = remainingEscrow,
                    Transactiontype = TransactionType.EscrowCredit,
                    Referencetable = ReferenceTable.Payment,
                    Referenceid = booking.Bookingid,
                    Description = txId,
                    Createdat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow
                });
            }

            // Remaining amount is now paid → activate sessions 2..N (were reserved until now).
            await ActivateRemainingSessionsAsync(bookingId, ct);

            await AddBookingPaymentTransactionIfMissingAsync(
                capture,
                booking,
                PaymentTransactionPurpose.BookingRemaining,
                amount,
                orderCode,
                txId,
                "Booking remaining payment.",
                ct);

            await context.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            logger.LogInformation("Remaining payment confirmed for booking {Id}, amount {Amount}", bookingId, amount);

            await SendPaymentPhaseNotificationsAsync(booking, isDepositPhase: false);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    private async Task AddBookingPaymentTransactionIfMissingAsync(
        PaymentTransactionCapture? capture,
        Booking booking,
        string purpose,
        int amount,
        long orderCode,
        string txId,
        string note,
        CancellationToken ct)
    {
        if (capture == null)
            return;

        var description = purpose == PaymentTransactionPurpose.BookingDeposit
            ? "Booking deposit payment"
            : "Booking remaining payment";

        var incoming = capture.Create(
            purpose,
            PaymentTransactionDirection.Inbound,
            amount,
            booking.Parentid,
            orderCode,
            txId,
            bookingId: booking.Bookingid,
            description: description,
            note: note);

        var providerTransactionId = incoming.Providertransactionid;
        var existing = await context.PaymentTransactions.FirstOrDefaultAsync(t =>
            t.Channel == capture.Channel
            && ((!string.IsNullOrWhiteSpace(providerTransactionId)
                    && t.Providertransactionid == providerTransactionId)
                || (t.Bookingid == booking.Bookingid
                    && t.Purpose == purpose
                    && t.Ordercode == orderCode)), ct);

        if (existing == null)
        {
            context.PaymentTransactions.Add(incoming);
            return;
        }

        // Polling may confirm first and webhook may arrive later. Keep one row per
        // PayOS order, then enrich the polling row with the signed webhook payload.
        if (capture.HasPayOSWebhook)
            EnrichPaymentTransaction(existing, incoming);
    }

    private static void EnrichPaymentTransaction(PaymentTransaction target, PaymentTransaction source)
    {
        target.Providertransactionid = source.Providertransactionid ?? target.Providertransactionid;
        target.Paymentlinkid = source.Paymentlinkid ?? target.Paymentlinkid;
        target.Description = source.Description ?? target.Description;
        target.Paidat = source.Paidat ?? target.Paidat;
        target.Note = source.Note ?? target.Note;
        target.Webhookcode = source.Webhookcode ?? target.Webhookcode;
        target.Webhookdesc = source.Webhookdesc ?? target.Webhookdesc;
        target.Webhooksuccess = source.Webhooksuccess ?? target.Webhooksuccess;
        target.Providercode = source.Providercode ?? target.Providercode;
        target.Providerdesc = source.Providerdesc ?? target.Providerdesc;
        target.Sourceaccountbankid = source.Sourceaccountbankid ?? target.Sourceaccountbankid;
        target.Sourceaccountbankname = source.Sourceaccountbankname ?? target.Sourceaccountbankname;
        target.Sourceaccountnumber = source.Sourceaccountnumber ?? target.Sourceaccountnumber;
        target.Sourceaccountname = source.Sourceaccountname ?? target.Sourceaccountname;
        target.Destinationaccountnumber = source.Destinationaccountnumber ?? target.Destinationaccountnumber;
        target.Destinationaccountname = source.Destinationaccountname ?? target.Destinationaccountname;
        target.Providerpayload = source.Providerpayload ?? target.Providerpayload;
    }

    private static void EnsureDepositAmountsCalculated(Booking booking)
    {
        var sessions = booking.Totalsessions ?? 1;
        var finalPrice = booking.Finalprice ?? 0;

        if (booking.Depositamount == null || booking.Depositamount == 0)
        {
            // Chưa tính gì — tính cả hai từ đầu
            var (deposit, remaining) = BookingFeeCalculator.CalculatePaymentPhases(finalPrice, sessions);
            booking.Depositamount = deposit;
            booking.Remainingamount = remaining;
        }
        else if (booking.Remainingamount == null)
        {
            // Deposit đã có nhưng Remaining chưa được lưu (data cũ bị bug) — bổ sung Remaining
            booking.Remainingamount = finalPrice - booking.Depositamount.Value;
        }
    }

    // Called when PayOS confirms payment AFTER the booking deadline has already passed.
    // Refunds the received amount to the parent's wallet instead of escrowing.
    // Idempotent: safe to call multiple times for the same txId (PayOS webhook retry).
    private async Task RefundOrphanPaymentToWalletAsync(Booking booking, int amount, string txId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(booking.Parentid))
        {
            logger.LogWarning("RefundOrphan: booking {Id} has no parentId, skipping.", booking.Bookingid);
            return;
        }

        if (await walletRepo.HasTransactionByDescriptionAsync(txId, ReferenceTable.Booking, ct))
        {
            logger.LogWarning("RefundOrphan: duplicate txId {TxId} for booking {Id}, skipping.", txId, booking.Bookingid);
            return;
        }

        var wallet = await walletRepo.GetOrCreateForUpdateAsync(booking.Parentid, ct);
        wallet.Balance = (wallet.Balance ?? 0) + amount;
        wallet.Lastupdated = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;

        walletRepo.AddTransaction(new Wallettransaction
        {
            Wallet = wallet,
            Amount = amount,
            Transactiontype = TransactionType.Refund,
            Referencetable = ReferenceTable.Booking,
            Referenceid = booking.Bookingid,
            Description = txId,
            Createdat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow
        });

        booking.Refundamount = (booking.Refundamount ?? 0) + amount;
        booking.Refundstatus = RefundStatus.Refunded;
        booking.Updatedat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
    }

}
