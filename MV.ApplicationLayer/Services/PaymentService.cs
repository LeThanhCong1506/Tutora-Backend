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
    IBankListService bankListService,
    ILogger<PaymentService> logger) : IPaymentService
{
    //fix link
    private readonly PayOSClient _payOS = payOS;
    private readonly PayOSLinkFactory _linkFactory = new(
        payOS,
        paymentSettings.Value.ReturnUrl,
        paymentSettings.Value.CancelUrl);

    private sealed record PaymentRecordResult(
        bool CanApply,
        bool IsDuplicate,
        decimal AmountToApply,
        bool CanRefundToBooking,
        string? StableProcessingKey = null);

    public async Task ProcessWebhookAsync(
        PaymentWebhookRequest request,
        string rawPayload,
        CancellationToken ct = default)
    {
        if (request?.Data == null)
            throw new BookingException(BookingErrorCodes.InvalidWebhookPayload, "Dữ liệu webhook không hợp lệ", 400);

        if (request.Code != PayOSWebhookCode.SuccessCode || !request.Success)
        {
            logger.LogWarning("Non-success webhook: code={Code}", request.Code);
            return;
        }

        var data = request.Data;
        var capture = PaymentTransactionCapture.FromPayOSWebhook(
            request,
            rawPayload);
        logger.LogInformation(
            "Processing webhook orderCode: {OrderCode}, reference: {Reference}, amount: {Amount}",
            data.OrderCode,
            data.Reference,
            data.Amount);
        await ConfirmPaymentInternalAsync(
            data.OrderCode,
            data.Amount,
            capture.GetProcessingKey(),
            capture,
            ct);
    }

    public async Task RecordUnmatchedPayOSWebhookAsync(
        PaymentWebhookRequest request,
        string rawPayload,
        CancellationToken ct = default)
    {
        if (request?.Data == null)
            throw new BookingException(
                BookingErrorCodes.InvalidWebhookPayload,
                "Dữ liệu webhook không hợp lệ",
                400);

        if (request.Code != PayOSWebhookCode.SuccessCode
            || !request.Success)
            return;

        var orderCode = request.Data.OrderCode;
        var purpose = OrderCodeHelper.IsBookingOrderCode(orderCode)
            ? PaymentTransactionPurpose.BookingDeposit
            : OrderCodeHelper.IsRemainingOrderCode(orderCode)
                ? PaymentTransactionPurpose.BookingRemaining
                : OrderCodeHelper.IsTopupOrderCode(orderCode)
                    ? PaymentTransactionPurpose.WalletTopup
                    : PaymentTransactionPurpose.UnmatchedPayOS;

        await RecordOrphanPayOSTransactionAsync(
            PaymentTransactionCapture.FromPayOSWebhook(
                request,
                rawPayload),
            purpose,
            "Verified PayOS webhook could not be routed to an existing business record",
            ct);
    }

    private async Task RecordOrphanPayOSTransactionAsync(
        PaymentTransactionCapture capture,
        string purpose,
        string reason,
        CancellationToken ct)
    {
        var incoming = capture.Create(
            purpose,
            PaymentTransactionDirection.Inbound,
            capture.ObservedAmount ?? 0,
            userId: null,
            capture.ObservedOrderCode,
            reconciliationStatus:
                PaymentReconciliationStatus.Orphan);
        var existing = await context.PaymentTransactions
            .FirstOrDefaultAsync(
                PaymentTransactionCapture.BuildIdentityMatchPredicate(
                    incoming),
                ct);

        if (existing != null)
        {
            var conflicts =
                (!string.IsNullOrWhiteSpace(
                        existing.Providertransactionid)
                    && !string.IsNullOrWhiteSpace(
                        incoming.Providertransactionid)
                    && existing.Providertransactionid
                        != incoming.Providertransactionid)
                || existing.Amount != incoming.Amount
                || !string.Equals(
                    existing.Currency,
                    incoming.Currency,
                    StringComparison.OrdinalIgnoreCase)
                || (existing.Ordercode.HasValue
                    && incoming.Ordercode.HasValue
                    && existing.Ordercode != incoming.Ordercode)
                || (!string.IsNullOrWhiteSpace(existing.Paymentlinkid)
                    && !string.IsNullOrWhiteSpace(
                        incoming.Paymentlinkid)
                    && existing.Paymentlinkid
                        != incoming.Paymentlinkid);

            if (conflicts)
            {
                if (!(existing.Note?.Contains(
                        "PayOS reference conflict",
                        StringComparison.OrdinalIgnoreCase) ?? false))
                {
                    AddPaymentReconciliationAlert(
                        existing,
                        null,
                        PaymentAlertType.ReferenceConflict,
                        reason);
                    existing.Note = string.IsNullOrWhiteSpace(existing.Note)
                        ? "PayOS reference conflict detected."
                        : $"{existing.Note} PayOS reference conflict detected.";
                }
            }
            else
            {
                EnrichPaymentTransaction(existing, incoming);
            }

            await context.SaveChangesAsync(ct);
            return;
        }

        context.PaymentTransactions.Add(incoming);
        AddPaymentReconciliationAlert(
            incoming,
            null,
            PaymentAlertType.OrphanTransaction,
            reason);
        await context.SaveChangesAsync(ct);
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

    public async Task ConfirmPaymentManuallyAsync(int bookingId, AdminConfirmPaymentRequest request, string? actorUserId = null, CancellationToken ct = default)
    {
        if (request == null
            || request.Amount <= 0
            || string.IsNullOrWhiteSpace(request.TransactionId)
            || !request.PaidAt.HasValue
            || string.IsNullOrWhiteSpace(request.Note))
            throw new BookingException(BookingErrorCodes.InvalidInput, "Dữ liệu đầu vào không hợp lệ", 400);

        var booking = await bookingRepo.FindTrackedAsync(bookingId, ct)
            ?? throw new BookingException(BookingErrorCodes.BookingNotFound, ApiMessages.BookingNotFound, 404);

        // Ensure deposit/remaining amounts are calculated for old bookings
        EnsureDepositAmountsCalculated(booking);

        var txId = request.TransactionId.Trim().ToUpperInvariant();

        if (await context.PaymentTransactions.AsNoTracking().AnyAsync(
            t => t.Paymentmethod == PaymentTransactionMethod.Manual
                && t.Providertransactionid != null
                && t.Providertransactionid.ToUpper() == txId,
            ct))
        {
            throw new BookingException(
                BookingErrorCodes.DuplicateTransaction,
                $"Transaction '{txId}' đã được xử lý trước đó",
                409);
        }

        var expectedAmount = booking.Depositpaidat == null
            ? booking.Depositamount ?? 0
            : booking.Remainingamount ?? 0;
        if (request.Amount != expectedAmount)
        {
            throw new BookingException(
                BookingErrorCodes.AmountMismatch,
                "Số tiền không khớp",
                409);
        }

        // Auto-detect phase: if deposit not paid yet, confirm deposit; otherwise confirm remaining
        var isDepositPhase = booking.Depositpaidat == null;
        long orderCode;
        if (isDepositPhase)
            orderCode = OrderCodeHelper.GenerateBookingOrderCode(bookingId);
        else
            orderCode = OrderCodeHelper.GenerateRemainingOrderCode(bookingId);

        // The phase/amount precheck above is advisory. Detach it so the inner
        // transaction reloads and row-locks the latest booking state.
        context.Bookings.Entry(booking).State = EntityState.Detached;
        var capture = PaymentTransactionCapture.FromManual(request.PaidAt, actorUserId, request.Note, txId);
        try
        {
            await ConfirmPaymentInternalAsync(
                orderCode,
                request.Amount,
                txId,
                capture,
                ct);
        }
        catch (DbUpdateException ex)
            when (IsProviderTransactionReferenceConflict(ex))
        {
            throw new BookingException(
                BookingErrorCodes.DuplicateTransaction,
                $"Transaction '{txId}' đã được xử lý trước đó",
                409);
        }

        var expectedPurpose = isDepositPhase
            ? PaymentTransactionPurpose.BookingDeposit
            : PaymentTransactionPurpose.BookingRemaining;
        var recordedManualTransaction = await context.PaymentTransactions
            .AsNoTracking()
            .Where(t => t.Paymentmethod == PaymentTransactionMethod.Manual
                && t.Providertransactionid != null
                && t.Providertransactionid.ToUpper() == txId)
            .OrderByDescending(t => t.Paymenttransactionid)
            .FirstOrDefaultAsync(ct);
        if (recordedManualTransaction == null
            || recordedManualTransaction.Bookingid != bookingId
            || recordedManualTransaction.Purpose != expectedPurpose
            || recordedManualTransaction.Reconciliationstatus
                != PaymentReconciliationStatus.Matched)
        {
            throw new BookingException(
                BookingErrorCodes.InvalidBookingStatus,
                "Giao dịch đã được lưu để đối soát nhưng không thể áp dụng vì trạng thái booking vừa thay đổi.",
                409);
        }

        await SupersedeActivePayOSRequestsAsync(
            bookingId,
            isDepositPhase
                ? PaymentRequestPhase.Deposit
                : PaymentRequestPhase.Remaining,
            "Booking payment was confirmed manually by staff.",
            ct);
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

        context.Bookings.Entry(booking).State = EntityState.Detached;
        await ConfirmPaymentInternalAsync(orderCode, amount, txId, null, ct);

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

        var currentPhase = booking.Depositpaidat == null
            ? PaymentRequestPhase.Deposit
            : PaymentRequestPhase.Remaining;
        var paymentRequest = await context.PaymentRequests
            .Where(r => r.Bookingid == bookingId
                && r.Provider == PaymentRequestProvider.PayOS)
            .OrderByDescending(r => r.Phase == currentPhase)
            .ThenByDescending(r => r.Createdat)
            .FirstOrDefaultAsync();

        if (paymentRequest == null)
            return baseResponse;

        try
        {
            await ReconcilePaymentRequestAsync(
                paymentRequest,
                CancellationToken.None);

            if (string.IsNullOrWhiteSpace(paymentRequest.Paymentlinkid)
                && !paymentRequest.Ordercode.HasValue)
            {
                return baseResponse;
            }

            var info = !string.IsNullOrWhiteSpace(
                paymentRequest.Paymentlinkid)
                ? await _payOS.PaymentRequests.GetAsync(
                    paymentRequest.Paymentlinkid)
                : await _payOS.PaymentRequests.GetAsync(
                    paymentRequest.Ordercode!.Value);

            var captures =
                PaymentTransactionCapture.FromPayOSPaymentLink(info);
            if (captures.Count > 0)
            {
                foreach (var capture in captures)
                {
                    await ConfirmPaymentInternalAsync(
                        info.OrderCode,
                        capture.ObservedAmount ?? 0,
                        capture.GetProcessingKey(),
                        capture,
                        CancellationToken.None);
                }
            }
            else if (NormalizePaymentRequestStatus(info.Status.ToString())
                == PaymentRequestStatus.Paid)
            {
                await MarkPaidRequestWithoutTransactionsAsync(
                    paymentRequest,
                    CancellationToken.None);
            }

            // The authorization read is no-tracking so a later locked confirm
            // cannot accidentally reuse stale values. Refresh explicitly for
            // the response after captures may have updated/refunded the booking.
            var refreshedBooking = await context.Bookings
                .AsNoTracking()
                .FirstAsync(b => b.Bookingid == bookingId);
            var refreshedExpired =
                refreshedBooking.Status == BookingStatus.PaymentTimeout
                || (refreshedBooking.Paymentdueat.HasValue
                    && refreshedBooking.Paymentdueat.Value
                        <= MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow);
            var refreshedRefunded =
                refreshedBooking.Refundstatus == RefundStatus.Refunded
                && (refreshedBooking.Refundamount ?? 0) > 0;

            return new PaymentStatusResponse
            {
                BookingId = bookingId,
                Status = info.Status.ToString(),
                Amount = (int)info.Amount,
                AmountPaid = (int)info.AmountPaid,
                AmountRemaining = (int)info.AmountRemaining,
                IsPaid = refreshedBooking.Status == BookingStatus.Paid
                    || (refreshedBooking.Depositpaidat != null
                        && refreshedBooking.Remainingpaidat != null),
                DepositAmount = refreshedBooking.Depositamount ?? 0,
                RemainingAmount = refreshedBooking.Remainingamount ?? 0,
                IsDepositPaid = refreshedBooking.Depositpaidat != null,
                IsRemainingPaid = refreshedBooking.Remainingpaidat != null,
                IsExpired = refreshedExpired,
                RefundedToWallet = refreshedRefunded,
                RefundAmount = refreshedBooking.Refundamount ?? 0
            };
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Failed to get PayOS status for booking {BookingId}",
                bookingId);
            return baseResponse;
        }
    }

    private async Task ProcessPaymentLinkTransactionsAsync(
        PaymentRequest paymentRequest,
        int bookingId,
        CancellationToken ct)
    {
        // Provider request fields/status are published by the locked reconcile
        // helper. The second GET below is read-only transaction evidence.
        await ReconcilePaymentRequestAsync(paymentRequest, ct);
        var info = !string.IsNullOrWhiteSpace(paymentRequest.Paymentlinkid)
            ? await _payOS.PaymentRequests.GetAsync(
                paymentRequest.Paymentlinkid)
            : await _payOS.PaymentRequests.GetAsync(
                paymentRequest.Ordercode!.Value);

        var captures = PaymentTransactionCapture.FromPayOSPaymentLink(info);
        if (captures.Count == 0)
        {
            if (NormalizePaymentRequestStatus(info.Status.ToString())
                == PaymentRequestStatus.Paid)
                await MarkPaidRequestWithoutTransactionsAsync(paymentRequest, ct);
            return;
        }

        foreach (var capture in captures)
        {
            await ConfirmPaymentInternalAsync(
                info.OrderCode,
                capture.ObservedAmount ?? 0,
                capture.GetProcessingKey(),
                capture,
                ct);
        }
    }

    public async Task ReconcilePaymentRequestByIdAsync(
        int paymentRequestId,
        CancellationToken ct = default)
    {
        var paymentRequest = await context.PaymentRequests
            .FirstOrDefaultAsync(r =>
                r.Paymentrequestid == paymentRequestId,
                ct);
        if (paymentRequest == null)
            return;

        if (paymentRequest.Ordercode == null
            && string.IsNullOrWhiteSpace(
                paymentRequest.Paymentlinkid))
        {
            await MarkPaymentRequestForReviewAsync(
                paymentRequest.Bookingid!.Value,
                paymentRequest,
                ct);
            return;
        }

        await ProcessPaymentLinkTransactionsAsync(
            paymentRequest,
            paymentRequest.Bookingid!.Value,
            ct);
    }

    private async Task MarkPaidRequestWithoutTransactionsAsync(
        PaymentRequest paymentRequest,
        CancellationToken ct)
    {
        await using var tx = await context.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable,
            ct);
        _ = await bookingRepo.FindWithRelationsForUpdateAsync(
            paymentRequest.Bookingid!.Value,
            ct);
        await context.PaymentRequests.Entry(paymentRequest).ReloadAsync(ct);
        paymentRequest.Status = PaymentRequestStatus.RequiresReview;
        paymentRequest.Updatedat =
            MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;

        var marker = $"\"paymentRequestId\":{paymentRequest.Paymentrequestid}";
        var alreadyAlerted = await context.Systemalerts.AsNoTracking()
            .AnyAsync(a => a.Type == PaymentAlertType.PaidWithoutTransaction
                && a.Metadata != null
                && a.Metadata.Contains(marker), ct);

        if (!alreadyAlerted)
        {
            context.Systemalerts.Add(new Systemalert
            {
                Type = PaymentAlertType.PaidWithoutTransaction,
                Severity = "High",
                Message =
                    "PayOS reports a paid payment request without transaction details.",
                Metadata = System.Text.Json.JsonSerializer.Serialize(new
                {
                    paymentRequestId =
                        paymentRequest.Paymentrequestid,
                    paymentRequest.Bookingid!.Value,
                    paymentRequest.Ordercode,
                    paymentRequest.Paymentlinkid
                }),
                Resolved = false,
                Createdat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow
            });
        }

        await context.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private async Task ConfirmPaymentInternalAsync(long orderCode, decimal amount, string txId, PaymentTransactionCapture? capture, CancellationToken ct)
    {
        bool isDeposit = OrderCodeHelper.IsBookingOrderCode(orderCode);
        bool isRemaining = OrderCodeHelper.IsRemainingOrderCode(orderCode);

        if (!isDeposit && !isRemaining)
            throw new BookingException(BookingErrorCodes.InvalidInput, "Kiểu mã đơn hàng không hợp lệ", 400);

        if (capture?.PaymentMethod == PaymentTransactionMethod.PayOS)
        {
            var bookingId = OrderCodeHelper.ExtractBookingId(orderCode);
            var bookingExists = await context.Bookings.AsNoTracking()
                .AnyAsync(b => b.Bookingid == bookingId, ct);
            if (!bookingExists)
            {
                await RecordOrphanPayOSTransactionAsync(
                    capture,
                    isDeposit
                        ? PaymentTransactionPurpose.BookingDeposit
                        : PaymentTransactionPurpose.BookingRemaining,
                    $"No booking exists for PayOS order code {orderCode}",
                    ct);
                return;
            }
        }

        if (isDeposit)
            await ConfirmDepositAsync(orderCode, amount, txId, capture, ct);
        else if (isRemaining)
            await ConfirmRemainingAsync(orderCode, amount, txId, capture, ct);
    }

    private async Task ConfirmDepositAsync(long orderCode, decimal amount, string txId, PaymentTransactionCapture? capture, CancellationToken ct)
    {
        // The booking row lock below is the serialization boundary for all
        // payment applications. READ COMMITTED lets a concurrent webhook or
        // polling request wait for that lock and then reload the winning state.
        // PostgreSQL SERIALIZABLE instead aborts the waiter with SQLSTATE
        // 40001 after the same row changes while it is waiting.
        await using var tx = await context.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.ReadCommitted,
            ct);
        try
        {
            var bookingId = OrderCodeHelper.ExtractBookingId(orderCode);
            var booking = await bookingRepo.FindWithRelationsForUpdateAsync(bookingId, ct)
                ?? throw new BookingException(BookingErrorCodes.BookingNotFound, ApiMessages.BookingNotFound, 404);

            EnsureDepositAmountsCalculated(booking);
            var depositAlreadyPaid = booking.Depositpaidat != null
                || booking.Paymentstatus == Escrowed
                || booking.Status == BookingStatus.Paid;
            var depositExpired = booking.Paymentdueat.HasValue
                && booking.Paymentdueat.Value
                    <= MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
            var depositBusinessEligible = !depositAlreadyPaid
                && (depositExpired
                || booking.Status == BookingStatus.Accepted
                || booking.Status == BookingStatus.PendingPayment);
            var recordResult = await RecordBookingPaymentTransactionAsync(
                capture,
                booking,
                PaymentTransactionPurpose.BookingDeposit,
                amount,
                booking.Depositamount ?? 0,
                orderCode,
                txId,
                "Booking deposit payment.",
                depositBusinessEligible,
                ct);

            // An expired PayOS booking must return every actual bank transfer
            // exactly once. Use the capture identity rather than the order code
            // so split/extra transfers are neither retained nor double-refunded.
            if (capture?.PaymentMethod == PaymentTransactionMethod.PayOS
                && depositExpired
                && recordResult.CanRefundToBooking)
            {
                var refunded = await RefundOrphanPaymentToWalletAsync(
                    booking,
                    capture.ObservedAmount ?? amount,
                    $"payos-expired-refund:{recordResult.StableProcessingKey ?? capture.GetProcessingKey()}",
                    ct);
                await context.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                if (refunded)
                {
                    logger.LogInformation(
                        "Expired PayOS deposit capture refunded to wallet for booking {Id}, amount {Amount}.",
                        bookingId,
                        capture.ObservedAmount ?? amount);
                    await SendRefundNotificationAsync(booking);
                }
                return;
            }

            if (recordResult.CanApply)
                amount = recordResult.AmountToApply;

            if (capture != null && !recordResult.CanApply)
            {
                var refunded = false;
                if (recordResult.CanRefundToBooking
                    && depositAlreadyPaid
                    && capture.PaymentMethod
                        == PaymentTransactionMethod.PayOS)
                {
                    refunded = await RefundOrphanPaymentToWalletAsync(
                        booking,
                        capture.ObservedAmount ?? amount,
                        $"payos-extra-refund:{recordResult.StableProcessingKey ?? capture.GetProcessingKey()}",
                        ct);
                }

                await context.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                if (refunded)
                    await SendRefundNotificationAsync(booking);
                return;
            }

            // Already deposit-paid or fully paid
            if (depositAlreadyPaid)
            {
                logger.LogWarning("Booking {Id} deposit already paid", bookingId);
                if (capture != null)
                {
                    await context.SaveChangesAsync(ct);
                    await tx.CommitAsync(ct);
                }
                return;
            }

            if (depositExpired)
            {
                var refundKey = capture?.PaymentMethod
                        == PaymentTransactionMethod.PayOS
                    ? $"payos-expired-refund:{orderCode}"
                    : txId;
                var refunded = await RefundOrphanPaymentToWalletAsync(
                    booking,
                    amount,
                    refundKey,
                    ct);
                await context.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                if (refunded)
                {
                    logger.LogInformation("Orphan deposit refunded to wallet for expired booking {Id}, amount {Amount}.", bookingId, amount);
                    await SendRefundNotificationAsync(booking);
                }
                return;
            }

            if (amount != (booking.Depositamount ?? 0))
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

    private async Task ConfirmRemainingAsync(long orderCode, decimal amount, string txId, PaymentTransactionCapture? capture, CancellationToken ct)
    {
        await using var tx = await context.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.ReadCommitted,
            ct);
        try
        {
            var bookingId = OrderCodeHelper.ExtractBookingId(orderCode);
            var booking = await bookingRepo.FindWithRelationsForUpdateAsync(bookingId, ct)
                ?? throw new BookingException(BookingErrorCodes.BookingNotFound, ApiMessages.BookingNotFound, 404);

            EnsureDepositAmountsCalculated(booking);
            var remainingAlreadyPaid = booking.Remainingpaidat != null
                || booking.Paymentstatus == Escrowed
                || booking.Status == BookingStatus.Paid;
            var remainingExpiredOrFinalized =
                (booking.Paymentdueat.HasValue
                    && booking.Paymentdueat.Value
                        <= MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow)
                || (booking.Status != BookingStatus.DepositPaid
                    && booking.Status != BookingStatus.PendingRemainingPayment
                    && booking.Status != BookingStatus.Ongoing);
            var remainingBusinessEligible = !remainingAlreadyPaid
                && (booking.Depositpaidat != null
                    && (remainingExpiredOrFinalized
                        || booking.Status == BookingStatus.DepositPaid
                        || booking.Status == BookingStatus.PendingRemainingPayment
                        || booking.Status == BookingStatus.Ongoing));
            var recordResult = await RecordBookingPaymentTransactionAsync(
                capture,
                booking,
                PaymentTransactionPurpose.BookingRemaining,
                amount,
                booking.Remainingamount ?? 0,
                orderCode,
                txId,
                "Booking remaining payment.",
                remainingBusinessEligible,
                ct);

            // Finalized/expired remaining payments follow the same per-capture
            // refund rule as deposits. This also covers a later extra transfer
            // after an earlier split payment was already refunded.
            if (capture?.PaymentMethod == PaymentTransactionMethod.PayOS
                && remainingExpiredOrFinalized
                && recordResult.CanRefundToBooking)
            {
                var refunded = await RefundOrphanPaymentToWalletAsync(
                    booking,
                    capture.ObservedAmount ?? amount,
                    $"payos-expired-refund:{recordResult.StableProcessingKey ?? capture.GetProcessingKey()}",
                    ct);
                await context.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                if (refunded)
                {
                    logger.LogInformation(
                        "Expired PayOS remaining capture refunded to wallet for booking {Id}, amount {Amount}.",
                        bookingId,
                        capture.ObservedAmount ?? amount);
                    await SendRefundNotificationAsync(booking);
                }
                return;
            }

            if (recordResult.CanApply)
                amount = recordResult.AmountToApply;

            if (capture != null && !recordResult.CanApply)
            {
                var refunded = false;
                if (recordResult.CanRefundToBooking
                    && remainingAlreadyPaid
                    && capture.PaymentMethod
                        == PaymentTransactionMethod.PayOS)
                {
                    refunded = await RefundOrphanPaymentToWalletAsync(
                        booking,
                        capture.ObservedAmount ?? amount,
                        $"payos-extra-refund:{recordResult.StableProcessingKey ?? capture.GetProcessingKey()}",
                        ct);
                }

                await context.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                if (refunded)
                    await SendRefundNotificationAsync(booking);
                return;
            }

            // Already fully paid
            if (remainingAlreadyPaid)
            {
                logger.LogWarning("Booking {Id} remaining already paid", bookingId);
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
            if (remainingExpiredOrFinalized)
            {
                var refundKey = capture?.PaymentMethod
                        == PaymentTransactionMethod.PayOS
                    ? $"payos-expired-refund:{orderCode}"
                    : txId;
                var refunded = await RefundOrphanPaymentToWalletAsync(
                    booking,
                    amount,
                    refundKey,
                    ct);
                await context.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                if (refunded)
                {
                    logger.LogInformation("Orphan remaining payment refunded to wallet for expired booking {Id}, amount {Amount}.", bookingId, amount);
                    await SendRefundNotificationAsync(booking);
                }
                return;
            }

            if (amount != (booking.Remainingamount ?? 0))
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

    private async Task<PaymentRecordResult> RecordBookingPaymentTransactionAsync(
        PaymentTransactionCapture? capture,
        Booking booking,
        string purpose,
        decimal amount,
        decimal expectedAmount,
        long orderCode,
        string txId,
        string note,
        bool allowBookingApplication,
        CancellationToken ct)
    {
        if (capture == null)
            return new PaymentRecordResult(true, false, amount, false);

        var description = purpose == PaymentTransactionPurpose.BookingDeposit
            ? "Booking deposit payment"
            : "Booking remaining payment";
        var phase = purpose == PaymentTransactionPurpose.BookingDeposit
            ? PaymentRequestPhase.Deposit
            : PaymentRequestPhase.Remaining;
        var observedOrderCode = capture.ObservedOrderCode ?? orderCode;
        PaymentRequest? paymentRequest = null;

        if (capture.PaymentMethod == PaymentTransactionMethod.PayOS)
        {
            paymentRequest = await context.PaymentRequests
                .FirstOrDefaultAsync(r =>
                    r.Provider == PaymentRequestProvider.PayOS
                    && r.Bookingid == booking.Bookingid
                    && (r.Phase == phase
                        || r.Phase
                            == PaymentRequestPhase.LegacyUnknown)
                    && ((capture.PaymentLinkId != null
                            && r.Paymentlinkid == capture.PaymentLinkId)
                        || (r.Ordercode != null
                            && r.Ordercode == observedOrderCode)), ct);

            if (paymentRequest == null)
            {
                paymentRequest = await context.PaymentRequests
                    .Where(r => r.Provider == PaymentRequestProvider.PayOS
                        && r.Bookingid == booking.Bookingid
                        && r.Phase == phase
                        && r.Ordercode == null
                        && r.Paymentlinkid == null)
                    .OrderByDescending(r => r.Createdat)
                    .FirstOrDefaultAsync(ct);
            }

            if (paymentRequest != null)
            {
                if (paymentRequest.Phase
                    == PaymentRequestPhase.LegacyUnknown)
                {
                    paymentRequest.Phase = phase;
                }
                paymentRequest.Ordercode ??= observedOrderCode;
                paymentRequest.Paymentlinkid ??= capture.PaymentLinkId;
                paymentRequest.Amount ??= expectedAmount;
                paymentRequest.Updatedat =
                    MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
            }
        }

        var incoming = capture.Create(
            purpose,
            PaymentTransactionDirection.Inbound,
            amount,
            booking.Parentid,
            capture.PaymentMethod == PaymentTransactionMethod.PayOS
                ? observedOrderCode
                : null,
            txId,
            bookingId: booking.Bookingid,
            description: description,
            destinationAccountNumber:
                paymentRequest?.Displayaccountnumber,
            destinationAccountName:
                paymentRequest?.Displayaccountname,
            note: note,
            paymentRequestId: paymentRequest?.Paymentrequestid,
            destinationBankBin: paymentRequest?.Destinationbankbin,
            destinationBankName: paymentRequest?.Destinationbankname);

        var existing = await context.PaymentTransactions
            .FirstOrDefaultAsync(
                PaymentTransactionCapture.BuildIdentityMatchPredicate(
                    incoming),
                ct);

        var isDuplicate = existing != null;
        PaymentTransaction transaction;
        if (existing != null)
        {
            var hasReferenceConflict =
                (!string.IsNullOrWhiteSpace(
                        existing.Providertransactionid)
                    && !string.IsNullOrWhiteSpace(
                        incoming.Providertransactionid)
                    && existing.Providertransactionid
                        != incoming.Providertransactionid)
                || (existing.Bookingid.HasValue
                    && existing.Bookingid != booking.Bookingid)
                || existing.Amount != incoming.Amount
                || !string.Equals(
                    existing.Currency,
                    incoming.Currency,
                    StringComparison.OrdinalIgnoreCase)
                || (existing.Paymentrequestid.HasValue
                    && paymentRequest != null
                    && existing.Paymentrequestid
                        != paymentRequest.Paymentrequestid)
                || (existing.Ordercode.HasValue
                    && incoming.Ordercode.HasValue
                    && existing.Ordercode != incoming.Ordercode)
                || (!string.IsNullOrWhiteSpace(existing.Paymentlinkid)
                    && !string.IsNullOrWhiteSpace(
                        incoming.Paymentlinkid)
                    && existing.Paymentlinkid != incoming.Paymentlinkid);

            if (hasReferenceConflict)
            {
                if (!(existing.Note?.Contains(
                        "PayOS reference conflict",
                        StringComparison.OrdinalIgnoreCase) ?? false))
                {
                    AddPaymentReconciliationAlert(
                        existing,
                        paymentRequest,
                        PaymentAlertType.ReferenceConflict,
                        "The same PayOS reference was observed with a different booking, order code, or payment link.");
                    existing.Note = string.IsNullOrWhiteSpace(existing.Note)
                        ? "PayOS reference conflict detected."
                        : $"{existing.Note} PayOS reference conflict detected.";
                }

                // A provider reference is immutable and can pay at most the
                // booking/payment request it was first linked to. Never let a
                // replay carrying another order/link apply money twice.
                return new PaymentRecordResult(false, true, 0, false);
            }

            if (paymentRequest != null)
            {
                existing.Paymentrequestid ??=
                    paymentRequest.Paymentrequestid;
                existing.Bookingid ??= booking.Bookingid;
                existing.Userid ??= booking.Parentid;
                if (existing.Purpose
                    == PaymentTransactionPurpose.UnmatchedPayOS)
                {
                    existing.Purpose = purpose;
                }
            }

            EnrichPaymentTransaction(existing, incoming);
            transaction = existing;
        }
        else
        {
            transaction = incoming;
            context.PaymentTransactions.Add(transaction);
        }

        if (capture.PaymentMethod == PaymentTransactionMethod.PayOS
            && transaction.Paymenttransactionid == 0)
        {
            // Refund idempotency uses the immutable database row id. Flush the
            // new audit row inside the caller's transaction before any refund;
            // a later failure still rolls this insert back atomically.
            await context.SaveChangesAsync(ct);
        }

        var stableProcessingKey = PaymentTransactionCapture
            .GetStableStoredProcessingKey(transaction);

        if (capture.PaymentMethod == PaymentTransactionMethod.Manual)
        {
            if (isDuplicate)
                return new PaymentRecordResult(false, true, 0, false);

            transaction.Reconciliationstatus = allowBookingApplication
                ? PaymentReconciliationStatus.Matched
                : PaymentReconciliationStatus.Unexpected;
            if (!allowBookingApplication)
            {
                AddPaymentReconciliationAlert(
                    transaction,
                    null,
                    PaymentAlertType.UnexpectedTransaction,
                    "The booking phase was already paid or changed before manual confirmation acquired the row lock");
                return new PaymentRecordResult(false, false, 0, false);
            }

            return new PaymentRecordResult(
                true,
                false,
                transaction.Amount,
                false);
        }

        if (paymentRequest == null)
        {
            transaction.Reconciliationstatus =
                PaymentReconciliationStatus.Orphan;
            if (!isDuplicate)
            {
                AddPaymentReconciliationAlert(
                    transaction,
                    null,
                    PaymentAlertType.OrphanTransaction,
                    PaymentReconciliationStatus.Orphan);
            }

            return new PaymentRecordResult(false, isDuplicate, 0, false);
        }

        transaction.Paymentrequestid =
            paymentRequest.Paymentrequestid;
        transaction.Bookingid ??= booking.Bookingid;
        transaction.Userid ??= booking.Parentid;

        var linkedTransactions = await context.PaymentTransactions
            .Where(t => t.Paymentrequestid
                == paymentRequest.Paymentrequestid)
            .OrderBy(t => t.Paymenttransactionid)
            .ToListAsync(ct);
        if (!linkedTransactions.Contains(transaction))
            linkedTransactions.Add(transaction);

        var expected = paymentRequest.Amount ?? expectedAmount;
        if (isDuplicate
            && transaction.Reconciliationstatus
                == PaymentReconciliationStatus.Matched)
        {
            paymentRequest.Status = PaymentRequestStatus.Paid;
            paymentRequest.Updatedat =
                MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
            return new PaymentRecordResult(
                true,
                true,
                expected,
                true,
                stableProcessingKey);
        }

        if (paymentRequest.Amount.HasValue
            && paymentRequest.Amount.Value != expectedAmount)
        {
            foreach (var linked in linkedTransactions)
            {
                if (linked.Reconciliationstatus
                    != PaymentReconciliationStatus.Unexpected)
                {
                    linked.Reconciliationstatus =
                        PaymentReconciliationStatus.AmountMismatch;
                }
            }

            paymentRequest.Status =
                PaymentRequestStatus.RequiresReview;
            paymentRequest.Updatedat =
                MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
            if (!isDuplicate)
            {
                AddPaymentReconciliationAlert(
                    transaction,
                    paymentRequest,
                    PaymentAlertType.AmountMismatch,
                    $"Payment request amount {paymentRequest.Amount.Value} differs from current booking amount {expectedAmount}");
            }

            return new PaymentRecordResult(
                false,
                isDuplicate,
                0,
                true,
                stableProcessingKey);
        }

        expected = expectedAmount;

        var otherMatched = linkedTransactions.Any(t =>
            !ReferenceEquals(t, transaction)
            && t.Reconciliationstatus
                == PaymentReconciliationStatus.Matched);

        if (otherMatched)
        {
            transaction.Reconciliationstatus =
                PaymentReconciliationStatus.Unexpected;
            paymentRequest.Status = PaymentRequestStatus.Paid;
            paymentRequest.Updatedat =
                MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
            if (!isDuplicate)
            {
                AddPaymentReconciliationAlert(
                    transaction,
                    paymentRequest,
                    PaymentAlertType.UnexpectedTransaction,
                    "An additional PayOS transaction arrived after this payment request was already matched");
            }

            return new PaymentRecordResult(
                false,
                isDuplicate,
                0,
                true,
                stableProcessingKey);
        }

        if (expected <= 0)
        {
            transaction.Reconciliationstatus =
                PaymentReconciliationStatus.Unexpected;
            paymentRequest.Status =
                PaymentRequestStatus.RequiresReview;
            paymentRequest.Updatedat =
                MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
            if (!isDuplicate)
            {
                AddPaymentReconciliationAlert(
                    transaction,
                    paymentRequest,
                    PaymentAlertType.UnexpectedTransaction,
                    "The payment request has no trustworthy expected amount");
            }

            return new PaymentRecordResult(
                false,
                isDuplicate,
                0,
                true,
                stableProcessingKey);
        }

        var receivedTotal = linkedTransactions.Sum(t => t.Amount);

        if (!allowBookingApplication)
        {
            transaction.Reconciliationstatus =
                PaymentReconciliationStatus.Unexpected;
            paymentRequest.Status =
                PaymentRequestStatus.RequiresReview;
            paymentRequest.Updatedat =
                MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
            if (!isDuplicate)
            {
                AddPaymentReconciliationAlert(
                    transaction,
                    paymentRequest,
                    PaymentAlertType.UnexpectedTransaction,
                    "The booking was already paid or is not eligible for this PayOS payment");
            }

            return new PaymentRecordResult(
                false,
                isDuplicate,
                0,
                true,
                stableProcessingKey);
        }

        if (receivedTotal < expected)
        {
            foreach (var linked in linkedTransactions)
            {
                if (linked.Reconciliationstatus
                    != PaymentReconciliationStatus.Unexpected)
                {
                    linked.Reconciliationstatus =
                        PaymentReconciliationStatus.Partial;
                }
            }

            paymentRequest.Status = paymentRequest.Status
                    == PaymentRequestStatus.Paid
                ? PaymentRequestStatus.RequiresReview
                : PaymentRequestStatus.Processing;
            paymentRequest.Updatedat =
                MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
            return new PaymentRecordResult(
                false,
                isDuplicate,
                0,
                true,
                stableProcessingKey);
        }

        if (receivedTotal > expected)
        {
            foreach (var linked in linkedTransactions)
            {
                if (linked.Reconciliationstatus
                    != PaymentReconciliationStatus.Unexpected)
                {
                    linked.Reconciliationstatus =
                        PaymentReconciliationStatus.AmountMismatch;
                }
            }

            paymentRequest.Status =
                PaymentRequestStatus.RequiresReview;
            paymentRequest.Updatedat =
                MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
            if (!isDuplicate)
            {
                AddPaymentReconciliationAlert(
                    transaction,
                    paymentRequest,
                    PaymentAlertType.AmountMismatch,
                    $"Received total {receivedTotal} differs from expected amount {expected}");
            }

            return new PaymentRecordResult(
                false,
                isDuplicate,
                0,
                true,
                stableProcessingKey);
        }

        foreach (var linked in linkedTransactions)
        {
            linked.Reconciliationstatus =
                PaymentReconciliationStatus.Matched;
        }

        paymentRequest.Status = PaymentRequestStatus.Paid;
        paymentRequest.Updatedat =
            MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
        return new PaymentRecordResult(
            true,
            isDuplicate,
            expected,
            true,
            stableProcessingKey);
    }

    private static void EnrichPaymentTransaction(PaymentTransaction target, PaymentTransaction source)
    {
        target.Providertransactionid ??=
            source.Providertransactionid;
        target.Capturefingerprint ??=
            source.Capturefingerprint;
        target.Paymentlinkid = source.Paymentlinkid ?? target.Paymentlinkid;
        target.Description ??= source.Description;
        target.Paidat ??= source.Paidat;
        target.Note ??= source.Note;
        target.Webhookcode = source.Webhookcode ?? target.Webhookcode;
        target.Webhookdesc = source.Webhookdesc ?? target.Webhookdesc;
        target.Webhooksuccess = source.Webhooksuccess ?? target.Webhooksuccess;
        target.Providercode = source.Providercode ?? target.Providercode;
        target.Providerdesc = source.Providerdesc ?? target.Providerdesc;
        target.Sourceaccountbankid = source.Sourceaccountbankid ?? target.Sourceaccountbankid;
        target.Sourceaccountbankname = source.Sourceaccountbankname ?? target.Sourceaccountbankname;
        target.Sourceaccountnumber = source.Sourceaccountnumber ?? target.Sourceaccountnumber;
        target.Sourceaccountname = source.Sourceaccountname ?? target.Sourceaccountname;
        target.Destinationaccountbankbin =
            source.Destinationaccountbankbin
            ?? target.Destinationaccountbankbin;
        target.Destinationaccountbankname =
            source.Destinationaccountbankname
            ?? target.Destinationaccountbankname;
        target.Destinationaccountnumber = source.Destinationaccountnumber ?? target.Destinationaccountnumber;
        target.Destinationaccountname = source.Destinationaccountname ?? target.Destinationaccountname;
        target.Destinationvirtualaccountnumber =
            source.Destinationvirtualaccountnumber
            ?? target.Destinationvirtualaccountnumber;
        target.Destinationvirtualaccountname =
            source.Destinationvirtualaccountname
            ?? target.Destinationvirtualaccountname;
        if (target.Providerpayload == null
            || source.Capturesource == PaymentCaptureSource.Polling)
        {
            target.Providerpayload =
                source.Providerpayload ?? target.Providerpayload;
        }
        target.Webhookpayload ??= source.Webhookpayload;
    }

    private void AddPaymentReconciliationAlert(
        PaymentTransaction transaction,
        PaymentRequest? paymentRequest,
        string alertType,
        string reason)
    {
        context.Systemalerts.Add(new Systemalert
        {
            Type = alertType,
            Severity = "High",
            Message =
                $"PayOS transaction requires reconciliation: {reason}.",
            Metadata = System.Text.Json.JsonSerializer.Serialize(new
            {
                paymentRequestId =
                    paymentRequest?.Paymentrequestid,
                paymentTransactionId =
                    transaction.Paymenttransactionid == 0
                        ? (int?)null
                        : transaction.Paymenttransactionid,
                transaction.Bookingid,
                transaction.Ordercode,
                transaction.Paymentlinkid,
                reference = transaction.Providertransactionid,
                transaction.Amount,
                expectedAmount = paymentRequest?.Amount,
                transaction.Capturesource,
                transaction.Capturefingerprint,
                reason
            }),
            Resolved = false,
            Createdat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow
        });
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
    private async Task<bool> RefundOrphanPaymentToWalletAsync(Booking booking, decimal amount, string txId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(booking.Parentid))
        {
            logger.LogWarning("RefundOrphan: booking {Id} has no parentId, skipping.", booking.Bookingid);
            return false;
        }

        if (await walletRepo.HasTransactionByDescriptionAsync(txId, ReferenceTable.Booking, ct))
        {
            logger.LogWarning("RefundOrphan: duplicate txId {TxId} for booking {Id}, skipping.", txId, booking.Bookingid);
            return false;
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
        return true;
    }

    private async Task SendRefundNotificationAsync(Booking booking)
    {
        if (string.IsNullOrWhiteSpace(booking.Parentid))
            return;

        try
        {
            await notificationService.CreateNotificationAsync(
                new NotificationRequest
                {
                    Userid = booking.Parentid,
                    Title = "Hoàn tiền thanh toán",
                    Message = "Khoản chuyển khoản không thể áp dụng cho booking và đã được hoàn vào ví của bạn.",
                    Type = NotificationType.PaymentRefundSuccess,
                    Referenceid = booking.Bookingid.ToString()
                });
        }
        catch (Exception ex)
        {
            // The wallet refund has already committed. Notification delivery
            // is best effort and must not turn a successful webhook into a
            // retry that attempts to roll back a completed transaction.
            logger.LogError(
                ex,
                "Could not send refund notification for booking {BookingId}.",
                booking.Bookingid);
        }
    }

    private static bool IsProviderTransactionReferenceConflict(
        DbUpdateException exception)
    {
        var message =
            $"{exception.Message} {exception.InnerException?.Message}";
        return message.Contains(
            "uq_payment_transactions_payment_method_provider_transaction_id",
            StringComparison.OrdinalIgnoreCase);
    }

}
