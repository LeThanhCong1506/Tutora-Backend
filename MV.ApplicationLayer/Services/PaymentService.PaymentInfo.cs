using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.Helpers;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Entities;
using MV.DomainLayer.Exceptions;
using static MV.DomainLayer.Constants.PaymentStatus;

namespace MV.ApplicationLayer.Services;

public partial class PaymentService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim>
        PaymentRequestCreationLocks = new();

    private static readonly JsonSerializerOptions PaymentPayloadJsonOptions =
        new(JsonSerializerDefaults.Web);

    public async Task<PaymentInfoResponse> GetPaymentInfoAsync(int bookingId, string userId)
    {
        var booking = await bookingRepo.FindForPaymentByUserAsync(bookingId, userId)
            ?? throw new BookingException(
                BookingErrorCodes.BookingNotFound,
                ApiMessages.BookingNotFound,
                404);

        if (booking.Paymentstatus == Escrowed || booking.Status == BookingStatus.Paid)
            throw new BookingException(
                BookingErrorCodes.BookingAlreadyPaid,
                "Booking đã được thanh toán rồi",
                409);

        EnsureDepositAmountsCalculated(booking);

        if (booking.Depositpaidat == null)
            return await GetDepositPaymentInfoAsync(booking, userId);

        if (booking.Remainingpaidat == null)
            return await GetRemainingPaymentInfoAsync(booking, userId);

        throw new BookingException(
            BookingErrorCodes.BookingAlreadyPaid,
            "Booking đã được thanh toán rồi",
            409);
    }

    /// <summary>
    /// Read-only payment summary for the current unpaid phase. Mirrors the validation
    /// of <see cref="GetPaymentInfoAsync"/> but never creates a PaymentRequest or calls
    /// PayOS — so opening the payment screen (and paying by wallet) leaves no PayOS
    /// artifact. EnsureDepositAmountsCalculated / fee computation mutate the tracked
    /// booking in memory only; this method never calls SaveChanges.
    /// </summary>
    public async Task<PaymentSummaryResponse> GetPaymentSummaryAsync(int bookingId, string userId)
    {
        var booking = await bookingRepo.FindForPaymentByUserAsync(bookingId, userId)
            ?? throw new BookingException(
                BookingErrorCodes.BookingNotFound,
                ApiMessages.BookingNotFound,
                404);

        if (booking.Paymentstatus == Escrowed || booking.Status == BookingStatus.Paid)
            throw new BookingException(
                BookingErrorCodes.BookingAlreadyPaid,
                "Booking đã được thanh toán rồi",
                409);

        EnsureDepositAmountsCalculated(booking);

        string phase;
        int amount;
        if (booking.Depositpaidat == null)
        {
            if (booking.Status != BookingStatus.Accepted
                && booking.Status != BookingStatus.PendingPayment)
            {
                throw new BookingException(
                    BookingErrorCodes.InvalidBookingStatus,
                    "Booking chưa ở trạng thái sẵn sàng để thanh toán",
                    409);
            }

            if (booking.Paymentdueat <= MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow)
            {
                throw new BookingException(
                    BookingErrorCodes.BookingExpired,
                    "Booking đã quá hạn thanh toán",
                    409);
            }

            if (booking.Finalprice == null || booking.Finalprice == 0)
            {
                var baseAmount = Math.Max(
                    (booking.Totalamount ?? 0) - (booking.Discountapplied ?? 0),
                    0);
                var (parentPct1, tutorPct1) = await commissionConfigService.GetFeePercentsAsync();
                var fees = BookingFeeCalculator.Calculate(baseAmount, parentPct1, tutorPct1);
                booking.Finalprice = fees.FinalPrice;
                booking.Parentfee = fees.ParentFee;
                booking.Platformfee = fees.PlatformFee;
                booking.Tutorfee = fees.TutorReceivable;
                EnsureDepositAmountsCalculated(booking);
            }

            phase = PaymentRequestPhase.Deposit;
            amount = (int)(booking.Depositamount ?? 0);
        }
        else if (booking.Remainingpaidat == null)
        {
            if (booking.Status != BookingStatus.DepositPaid
                && booking.Status != BookingStatus.PendingRemainingPayment
                && booking.Status != BookingStatus.Ongoing)
            {
                throw new BookingException(
                    BookingErrorCodes.InvalidBookingStatus,
                    "Booking chưa ở trạng thái sẵn sàng để thanh toán phần còn lại",
                    409);
            }

            phase = PaymentRequestPhase.Remaining;
            amount = (int)(booking.Remainingamount ?? 0);
        }
        else
        {
            throw new BookingException(
                BookingErrorCodes.BookingAlreadyPaid,
                "Booking đã được thanh toán rồi",
                409);
        }

        if (amount <= 0)
        {
            throw new BookingException(
                BookingErrorCodes.BookingAlreadyPaid,
                "Booking đã được thanh toán rồi",
                409);
        }

        var wallet = await walletRepo.GetByUserIdAsNoTrackingAsync(userId);
        var walletBalance = wallet?.Balance ?? 0;

        return new PaymentSummaryResponse
        {
            BookingId = booking.Bookingid,
            Amount = amount,
            PaymentPhase = phase,
            TotalAmount = booking.Finalprice ?? 0,
            DepositAmount = booking.Depositamount ?? 0,
            RemainingAmount = booking.Remainingamount ?? 0,
            IsDepositPaid = booking.Depositpaidat != null,
            IsRemainingPaid = booking.Remainingpaidat != null,
            WalletBalance = walletBalance,
            CanPayWithWallet = walletBalance >= amount,
            ExpiredAt = booking.Paymentdueat
        };
    }

    private async Task<PaymentInfoResponse> GetDepositPaymentInfoAsync(
        Booking booking,
        string userId)
    {
        if (booking.Status != BookingStatus.Accepted
            && booking.Status != BookingStatus.PendingPayment)
        {
            throw new BookingException(
                BookingErrorCodes.InvalidBookingStatus,
                "Booking chưa ở trạng thái sẵn sàng để thanh toán",
                409);
        }

        if (booking.Paymentdueat <= MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow)
        {
            throw new BookingException(
                BookingErrorCodes.BookingExpired,
                "Booking đã quá hạn thanh toán",
                409);
        }

        if (booking.Finalprice == null || booking.Finalprice == 0)
        {
            var baseAmount = Math.Max(
                (booking.Totalamount ?? 0) - (booking.Discountapplied ?? 0),
                0);
            var (parentPct1, tutorPct1) = await commissionConfigService.GetFeePercentsAsync();
            var fees = BookingFeeCalculator.Calculate(baseAmount, parentPct1, tutorPct1);
            booking.Finalprice = fees.FinalPrice;
            booking.Parentfee = fees.ParentFee;
            booking.Platformfee = fees.PlatformFee;
            booking.Tutorfee = fees.TutorReceivable;
            EnsureDepositAmountsCalculated(booking);
        }

        return await GetOrCreatePaymentRequestInfoAsync(
            booking,
            userId,
            PaymentRequestPhase.Deposit,
            (int)(booking.Depositamount ?? 0),
            booking.Paymentdueat,
            $"Coc Booking #{booking.Bookingid}");
    }

    private async Task<PaymentInfoResponse> GetRemainingPaymentInfoAsync(
        Booking booking,
        string userId)
    {
        if (booking.Status != BookingStatus.DepositPaid
            && booking.Status != BookingStatus.PendingRemainingPayment
            && booking.Status != BookingStatus.Ongoing)
        {
            throw new BookingException(
                BookingErrorCodes.InvalidBookingStatus,
                "Booking chưa ở trạng thái sẵn sàng để thanh toán phần còn lại",
                409);
        }

        var remainingDueAtFallback = booking.Paymentdueat ?? RemainingPaymentDeadlinePolicy.ComputeDeadline(
            MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow,
            await GetEarliestReservedSessionStartAsync(booking.Bookingid));

        return await GetOrCreatePaymentRequestInfoAsync(
            booking,
            userId,
            PaymentRequestPhase.Remaining,
            (int)(booking.Remainingamount ?? 0),
            remainingDueAtFallback,
            $"Tra not Booking #{booking.Bookingid}");
    }

    /// <summary>Dùng cho fallback khi Booking.Paymentdueat chưa từng được gán (không nên xảy ra ở
    /// đường bình thường, chỉ phòng hộ) — xem RemainingPaymentDeadlinePolicy.</summary>
    private Task<DateTime?> GetEarliestReservedSessionStartAsync(int bookingId)
        => context.ClassSessions
            .Where(x => x.Bookingid == bookingId && x.Status == ClassSessionStatus.Reserved)
            .OrderBy(x => x.Scheduledstart)
            .Select(x => (DateTime?)x.Scheduledstart)
            .FirstOrDefaultAsync();

    private async Task<PaymentInfoResponse> GetOrCreatePaymentRequestInfoAsync(
        Booking booking,
        string userId,
        string phase,
        int amount,
        DateTime? expiresAt,
        string description)
    {
        // Chặn TRƯỚC khi tạo link/QR PayOS — một khi có link thì không kiểm soát được việc
        // chuyển khoản ngân hàng nữa. Cùng điều kiện với PayWithWalletCoreAsync.
        if (string.IsNullOrWhiteSpace(booking.Parentid) && amount >= LargeTransactionPolicy.ThresholdAmount)
        {
            var approved = await largeTransactionOtpService.IsApprovedAsync(booking.Bookingid, phase);
            if (!approved)
                throw new LargeTransactionOtpRequiredException(phase);
        }

        var lockKey = $"{booking.Bookingid}:{phase}";
        var gate = PaymentRequestCreationLocks.GetOrAdd(
            lockKey,
            _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            return await GetOrCreatePaymentRequestInfoCoreAsync(
                booking,
                userId,
                phase,
                amount,
                expiresAt,
                description);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<PaymentInfoResponse> GetOrCreatePaymentRequestInfoCoreAsync(
        Booking booking,
        string userId,
        string phase,
        int amount,
        DateTime? expiresAt,
        string description)
    {
        if (amount <= 0)
        {
            throw new BookingException(
                BookingErrorCodes.BookingAlreadyPaid,
                "Booking đã được thanh toán rồi",
                409);
        }

        var wallet = await walletRepo.GetByUserIdAsNoTrackingAsync(userId);
        var request = await FindActivePaymentRequestAsync(
            booking.Bookingid,
            phase,
            CancellationToken.None);

        if (request != null)
        {
            if (request.Status == PaymentRequestStatus.Pending
                && string.IsNullOrWhiteSpace(request.Paymentlinkid)
                && request.Createdat
                    > MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow.AddMinutes(-2))
            {
                throw new BookingException(
                    BookingErrorCodes.InvalidBookingStatus,
                    "Link thanh toán đang được tạo. Vui lòng thử lại sau.",
                    409);
            }

            await ReconcilePaymentRequestAsync(request, CancellationToken.None);

            if (request.Phase != phase)
                request = null;
            else if (request.Status == PaymentRequestStatus.Paid)
            {
                await ProcessPaymentLinkTransactionsAsync(
                    request,
                    booking.Bookingid,
                    CancellationToken.None);
                throw new BookingException(
                    BookingErrorCodes.BookingAlreadyPaid,
                    "Booking đã được thanh toán rồi",
                    409);
            }
            else if (request.Status is PaymentRequestStatus.Pending
                     or PaymentRequestStatus.Processing
                     or PaymentRequestStatus.Unknown)
            {
                if (!string.IsNullOrWhiteSpace(request.Paymentlinkid)
                    && !string.IsNullOrWhiteSpace(request.Displayaccountnumber))
                {
                    return await BuildValidatedReusablePaymentInfoResponseAsync(
                        request,
                        wallet,
                        userId,
                        phase,
                        CancellationToken.None);
                }

                if (!string.IsNullOrWhiteSpace(request.Paymentlinkid))
                {
                    await CancelIncompletePaymentRequestAsync(
                        request,
                        "Missing cached PayOS display fields; replacing payment link.",
                        CancellationToken.None);
                    request = null;
                }
                else
                {
                    await MarkPaymentRequestForReviewAsync(
                        request.Bookingid!.Value,
                        request,
                        CancellationToken.None);
                    throw new BookingException(
                        BookingErrorCodes.InvalidBookingStatus,
                        "Link thanh toán đang được đối soát. Vui lòng thử lại sau.",
                        409);
                }
            }
            else if (request.Status == PaymentRequestStatus.RequiresReview)
            {
                throw new BookingException(
                    BookingErrorCodes.InvalidBookingStatus,
                    "Link thanh toán đang được đối soát. Vui lòng thử lại sau.",
                    409);
            }
            else
            {
                request = null;
            }
        }

        if (request == null)
        {
            var unresolvedLegacy = await context.PaymentRequests
                .Where(r => r.Bookingid == booking.Bookingid
                    && r.Provider == PaymentRequestProvider.PayOS
                    && r.Phase == PaymentRequestPhase.LegacyUnknown
                    && (r.Status == PaymentRequestStatus.Unknown
                        || r.Status == PaymentRequestStatus.RequiresReview))
                .OrderByDescending(r => r.Createdat)
                .FirstOrDefaultAsync();

            if (unresolvedLegacy != null)
            {
                await ReconcilePaymentRequestAsync(
                    unresolvedLegacy,
                    CancellationToken.None);

                if (unresolvedLegacy.Phase == phase)
                {
                    if (unresolvedLegacy.Status == PaymentRequestStatus.Paid)
                    {
                        await ProcessPaymentLinkTransactionsAsync(
                            unresolvedLegacy,
                            booking.Bookingid,
                            CancellationToken.None);
                        throw new BookingException(
                            BookingErrorCodes.BookingAlreadyPaid,
                            "Booking đã được thanh toán rồi",
                            409);
                    }

                    if (!string.IsNullOrWhiteSpace(
                            unresolvedLegacy.Displayaccountnumber))
                    {
                        return await BuildValidatedReusablePaymentInfoResponseAsync(
                            unresolvedLegacy,
                            wallet,
                            userId,
                            phase,
                            CancellationToken.None);
                    }
                }

                if (unresolvedLegacy.Phase == PaymentRequestPhase.LegacyUnknown)
                {
                    throw new BookingException(
                        BookingErrorCodes.InvalidBookingStatus,
                        "Link thanh toán cũ đang được đối soát. Vui lòng thử lại sau.",
                        409);
                }
            }

            // Phase 1: reserve a single request under the booking row lock, then
            // release the database transaction before calling PayOS/VietQR.
            // Wallet/manual/webhook flows use the same row lock and will either
            // happen before this reservation or see and supersede it afterwards.
            {
                await using var creationTx = await context.Database
                    .BeginTransactionAsync(
                        System.Data.IsolationLevel.Serializable,
                        CancellationToken.None);
                var validated = await LockAndValidateBookingForPaymentRequestAsync(
                    booking.Bookingid,
                    userId,
                    phase,
                    CancellationToken.None);
                booking = validated.Booking;
                amount = validated.Amount;
                expiresAt = validated.ExpiresAt;
                description = validated.Description;

                var concurrentlyCreated = await FindActivePaymentRequestAsync(
                    booking.Bookingid,
                    phase,
                    CancellationToken.None);
                if (concurrentlyCreated != null)
                {
                    await creationTx.CommitAsync();
                    if (!string.IsNullOrWhiteSpace(
                            concurrentlyCreated.Paymentlinkid)
                        && !string.IsNullOrWhiteSpace(
                            concurrentlyCreated.Displayaccountnumber))
                    {
                        return BuildPaymentInfoResponse(
                            booking,
                            concurrentlyCreated,
                            wallet);
                    }

                    throw new BookingException(
                        BookingErrorCodes.InvalidBookingStatus,
                        "Link thanh toán đang được tạo hoặc đối soát. Vui lòng thử lại sau.",
                        409);
                }

                request = await CreatePendingPaymentRequestAsync(
                    booking,
                    userId,
                    phase,
                    amount,
                    expiresAt);

                booking.Status = phase == PaymentRequestPhase.Deposit
                    ? BookingStatus.PendingPayment
                    : booking.Status;
                booking.Updatedat =
                    MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
                await context.SaveChangesAsync();
                await creationTx.CommitAsync();
            }

            var expiredAtUnix = expiresAt.HasValue
                ? (int)new DateTimeOffset(
                    DateTime.SpecifyKind(expiresAt.Value, DateTimeKind.Utc))
                    .ToUnixTimeSeconds()
                : (int)DateTimeOffset.UtcNow.AddHours(24).ToUnixTimeSeconds();

            try
            {
                var link = await _linkFactory.CreatePaymentLink(
                    request.Ordercode!.Value,
                    amount,
                    description,
                    expiredAtUnix);
                var destinationBankBin = NormalizeOptional(link.Bin);
                var destinationBankName =
                    await ResolveDestinationBankNameAsync(
                        destinationBankBin,
                        CancellationToken.None);
                var providerStatus = NormalizePaymentRequestStatus(
                    link.Status.ToString());
                var providerPayload = JsonSerializer.Serialize(
                    link,
                    PaymentPayloadJsonOptions);

                // Phase 2: publish the provider snapshot only after another
                // locked read. If an alternate payment won while PayOS was in
                // flight, preserve that booking state and cancel this new link.
                bool canPublish;
                bool shouldCancel;
                await using (var publishTx = await context.Database
                    .BeginTransactionAsync(
                        System.Data.IsolationLevel.Serializable,
                        CancellationToken.None))
                {
                    booking = await bookingRepo.FindWithRelationsForUpdateAsync(
                            booking.Bookingid,
                            CancellationToken.None)
                        ?? throw new BookingException(
                            BookingErrorCodes.BookingNotFound,
                            ApiMessages.BookingNotFound,
                            404);
                    await context.PaymentRequests.Entry(request).ReloadAsync();

                    var requestWasActive =
                        PaymentRequestStatus.IsActive(request.Status);
                    canPublish = requestWasActive
                        && IsBookingPhaseStillPayable(booking, phase);

                    request.Paymentlinkid = link.PaymentLinkId;
                    request.Amount = amount;
                    request.Currency = string.IsNullOrWhiteSpace(link.Currency)
                    ? Currency.Vnd
                    : link.Currency;
                    request.Destinationbankbin = destinationBankBin;
                    request.Destinationbankname = destinationBankName;
                    request.Displayaccountnumber = NormalizeOptional(
                        link.AccountNumber);
                    request.Displayaccountname = NormalizeOptional(
                        link.AccountName);
                    request.Description = NormalizeOptional(link.Description);
                    request.Checkouturl = NormalizeOptional(link.CheckoutUrl);
                    request.Qrcode = NormalizeOptional(link.QrCode);
                    request.Providerpayload = providerPayload;
                    request.Updatedat =
                        MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;

                    if (canPublish
                        || providerStatus == PaymentRequestStatus.Paid)
                    {
                        request.Status = providerStatus;
                    }
                    else if (request.Status is not (
                        PaymentRequestStatus.Superseded
                        or PaymentRequestStatus.Cancelled
                        or PaymentRequestStatus.Expired))
                    {
                        request.Status = PaymentRequestStatus.RequiresReview;
                    }

                    shouldCancel = !canPublish
                        && providerStatus != PaymentRequestStatus.Paid
                        && request.Status is not (
                            PaymentRequestStatus.Cancelled
                            or PaymentRequestStatus.Expired);

                    await context.SaveChangesAsync();
                    await publishTx.CommitAsync();
                }

                if (!canPublish)
                {
                    if (shouldCancel)
                    {
                        await CancelIncompletePaymentRequestAsync(
                            request,
                            "Booking was paid or changed while the PayOS link was being created.",
                            CancellationToken.None);
                    }

                    throw new BookingException(
                        BookingErrorCodes.BookingAlreadyPaid,
                        "Booking đã được thanh toán hoặc thay đổi trong lúc tạo link PayOS",
                        409);
                }

                logger.LogInformation(
                    "Created PayOS request {PaymentRequestId}, link {PaymentLinkId} for booking {BookingId}, phase {Phase}, amount {Amount}.",
                    request.Paymentrequestid,
                    request.Paymentlinkid,
                    booking.Bookingid,
                    phase,
                    amount);

                return BuildPaymentInfoResponse(booking, request, wallet);
            }
            catch (BookingException)
            {
                throw;
            }
            catch (Exception ex)
            {
                await MarkPaymentRequestForReviewAsync(
                    booking.Bookingid,
                    request,
                    CancellationToken.None);

                logger.LogError(
                    ex,
                    "Failed creating PayOS link for payment request {PaymentRequestId}.",
                    request.Paymentrequestid);
                throw new BookingException(
                    BookingErrorCodes.InvalidInput,
                    "Tạo link thanh toán thất bại; yêu cầu đã được giữ để đối soát.",
                    500);
            }
        }

        return await BuildValidatedReusablePaymentInfoResponseAsync(
            request,
            wallet,
            userId,
            phase,
            CancellationToken.None);
    }

    private async Task<PaymentInfoResponse>
        BuildValidatedReusablePaymentInfoResponseAsync(
            PaymentRequest request,
            Wallet? wallet,
            string userId,
            string phase,
            CancellationToken ct)
    {
        Booking booking;
        bool canReturn;
        bool shouldCancel;
        await using (var tx = await context.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable,
            ct))
        {
            booking = await bookingRepo.FindWithRelationsForUpdateAsync(
                    request.Bookingid!.Value,
                    ct)
                ?? throw new BookingException(
                    BookingErrorCodes.BookingNotFound,
                    ApiMessages.BookingNotFound,
                    404);
            await context.PaymentRequests.Entry(request).ReloadAsync(ct);

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

            var bookingPayable = IsBookingPhaseStillPayable(
                booking,
                phase);
            canReturn = bookingPayable
                && request.Status is (
                    PaymentRequestStatus.Pending
                    or PaymentRequestStatus.Processing
                    or PaymentRequestStatus.Unknown)
                && !string.IsNullOrWhiteSpace(request.Paymentlinkid)
                && !string.IsNullOrWhiteSpace(
                    request.Displayaccountnumber);
            shouldCancel = !bookingPayable
                && request.Status is (
                    PaymentRequestStatus.Pending
                    or PaymentRequestStatus.Processing
                    or PaymentRequestStatus.RequiresReview
                    or PaymentRequestStatus.Unknown)
                && (request.Ordercode.HasValue
                    || !string.IsNullOrWhiteSpace(
                        request.Paymentlinkid));
            await tx.CommitAsync(ct);
        }

        if (canReturn)
            return BuildPaymentInfoResponse(booking, request, wallet);

        if (shouldCancel)
        {
            await CancelIncompletePaymentRequestAsync(
                request,
                "Booking was paid or changed before an existing PayOS link could be returned.",
                ct);
        }

        throw new BookingException(
            request.Status == PaymentRequestStatus.Paid
                ? BookingErrorCodes.BookingAlreadyPaid
                : BookingErrorCodes.InvalidBookingStatus,
            request.Status == PaymentRequestStatus.Paid
                ? "Booking đã được thanh toán rồi"
                : "Link thanh toán không còn khả dụng; yêu cầu đang được đối soát.",
            409);
    }

    private static bool IsBookingPhaseStillPayable(
        Booking booking,
        string phase)
    {
        if (booking.Paymentstatus == Escrowed
            || booking.Status == BookingStatus.Paid)
        {
            return false;
        }

        var now = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
        if (phase == PaymentRequestPhase.Deposit)
        {
            return booking.Depositpaidat == null
                && (booking.Status == BookingStatus.Accepted
                    || booking.Status == BookingStatus.PendingPayment)
                && (!booking.Paymentdueat.HasValue
                    || booking.Paymentdueat.Value > now);
        }

        return booking.Depositpaidat != null
            && booking.Remainingpaidat == null
            && (booking.Status == BookingStatus.DepositPaid
                || booking.Status == BookingStatus.PendingRemainingPayment
                || booking.Status == BookingStatus.Ongoing)
            && (!booking.Paymentdueat.HasValue
                || booking.Paymentdueat.Value > now);
    }

    private async Task MarkPaymentRequestForReviewAsync(
        int bookingId,
        PaymentRequest request,
        CancellationToken ct)
    {
        try
        {
            await using var tx = await context.Database.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable,
                ct);
            _ = await bookingRepo.FindWithRelationsForUpdateAsync(
                bookingId,
                ct);
            await context.PaymentRequests.Entry(request).ReloadAsync(ct);
            if (PaymentRequestStatus.IsActive(request.Status))
            {
                request.Status = PaymentRequestStatus.RequiresReview;
                request.Updatedat =
                    MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
                await context.SaveChangesAsync(ct);
            }
            await tx.CommitAsync(ct);
        }
        catch (Exception persistException)
        {
            logger.LogError(
                persistException,
                "Could not mark failed PayOS request {PaymentRequestId} for review.",
                request.Paymentrequestid);
        }
    }

    private async Task<(
        Booking Booking,
        int Amount,
        DateTime? ExpiresAt,
        string Description)> LockAndValidateBookingForPaymentRequestAsync(
            int bookingId,
            string userId,
            string phase,
            CancellationToken ct)
    {
        var booking = await bookingRepo.FindWithRelationsForUpdateAsync(
                bookingId,
                ct)
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

        if (booking.Paymentstatus == Escrowed
            || booking.Status == BookingStatus.Paid)
        {
            throw new BookingException(
                BookingErrorCodes.BookingAlreadyPaid,
                "Booking đã được thanh toán rồi",
                409);
        }

        if (phase == PaymentRequestPhase.Deposit)
        {
            if (booking.Depositpaidat != null)
            {
                throw new BookingException(
                    BookingErrorCodes.BookingAlreadyPaid,
                    "Khoản cọc đã được thanh toán rồi",
                    409);
            }

            if (booking.Status != BookingStatus.Accepted
                && booking.Status != BookingStatus.PendingPayment)
            {
                throw new BookingException(
                    BookingErrorCodes.InvalidBookingStatus,
                    "Booking chưa ở trạng thái sẵn sàng để thanh toán",
                    409);
            }

            if (booking.Paymentdueat <=
                MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow)
            {
                throw new BookingException(
                    BookingErrorCodes.BookingExpired,
                    "Booking đã quá hạn thanh toán",
                    409);
            }

            if (booking.Finalprice == null || booking.Finalprice == 0)
            {
                var baseAmount = Math.Max(
                    (booking.Totalamount ?? 0)
                    - (booking.Discountapplied ?? 0),
                    0);
                var (parentPct1, tutorPct1) = await commissionConfigService.GetFeePercentsAsync();
                var fees = BookingFeeCalculator.Calculate(baseAmount, parentPct1, tutorPct1);
                booking.Finalprice = fees.FinalPrice;
                booking.Parentfee = fees.ParentFee;
                booking.Platformfee = fees.PlatformFee;
                booking.Tutorfee = fees.TutorReceivable;
            }

            EnsureDepositAmountsCalculated(booking);
            var amount = (int)(booking.Depositamount ?? 0);
            if (amount <= 0)
            {
                throw new BookingException(
                    BookingErrorCodes.BookingAlreadyPaid,
                    "Booking đã được thanh toán rồi",
                    409);
            }

            return (
                booking,
                amount,
                booking.Paymentdueat,
                $"Coc Booking #{booking.Bookingid}");
        }

        EnsureDepositAmountsCalculated(booking);
        if (booking.Depositpaidat == null)
        {
            throw new BookingException(
                BookingErrorCodes.InvalidBookingStatus,
                "Chưa thanh toán cọc",
                409);
        }

        if (booking.Remainingpaidat != null)
        {
            throw new BookingException(
                BookingErrorCodes.BookingAlreadyPaid,
                "Khoản còn lại đã được thanh toán rồi",
                409);
        }

        if (booking.Status != BookingStatus.DepositPaid
            && booking.Status != BookingStatus.PendingRemainingPayment
            && booking.Status != BookingStatus.Ongoing)
        {
            throw new BookingException(
                BookingErrorCodes.InvalidBookingStatus,
                "Booking chưa ở trạng thái sẵn sàng để thanh toán phần còn lại",
                409);
        }

        var remainingDueAt = booking.Paymentdueat
            ?? RemainingPaymentDeadlinePolicy.ComputeDeadline(
                MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow,
                await GetEarliestReservedSessionStartAsync(booking.Bookingid));
        if (remainingDueAt <= MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow)
        {
            throw new BookingException(
                BookingErrorCodes.BookingExpired,
                "Booking đã quá hạn thanh toán phần còn lại",
                409);
        }

        var remainingAmount = (int)(booking.Remainingamount ?? 0);
        if (remainingAmount <= 0)
        {
            throw new BookingException(
                BookingErrorCodes.BookingAlreadyPaid,
                "Booking đã được thanh toán rồi",
                409);
        }

        return (
            booking,
            remainingAmount,
            remainingDueAt,
            $"Tra not Booking #{booking.Bookingid}");
    }

    private async Task<PaymentRequest> CreatePendingPaymentRequestAsync(
        Booking booking,
        string userId,
        string phase,
        int amount,
        DateTime? expiresAt)
    {
        long? orderCode = null;
        for (var attempt = 0; attempt < 32; attempt++)
        {
            var candidate = phase == PaymentRequestPhase.Deposit
                ? OrderCodeHelper.GenerateBookingOrderCode(booking.Bookingid)
                : OrderCodeHelper.GenerateRemainingOrderCode(booking.Bookingid);
            var exists = await context.PaymentRequests.AsNoTracking()
                .AnyAsync(r => r.Provider == PaymentRequestProvider.PayOS
                    && r.Ordercode == candidate);
            if (!exists)
            {
                orderCode = candidate;
                break;
            }
        }

        if (!orderCode.HasValue)
        {
            throw new BookingException(
                BookingErrorCodes.InvalidInput,
                "Không thể cấp mã PayOS duy nhất cho booking. Vui lòng thử lại.",
                409);
        }
        var now = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
        var request = new PaymentRequest
        {
            Bookingid = booking.Bookingid,
            Userid = userId,
            Provider = PaymentRequestProvider.PayOS,
            Phase = phase,
            Ordercode = orderCode.Value,
            Amount = amount,
            Currency = Currency.Vnd,
            Status = PaymentRequestStatus.Pending,
            Expiresat = expiresAt,
            Createdat = now,
            Updatedat = now
        };

        context.PaymentRequests.Add(request);
        // The caller holds the booking row lock and already rechecked the
        // active-request index. Do not attempt another query after a PostgreSQL
        // constraint violation: that transaction is aborted and must roll back.
        await context.SaveChangesAsync();
        return request;
    }

    private async Task<PaymentRequest?> FindActivePaymentRequestAsync(
        int bookingId,
        string phase,
        CancellationToken ct)
    {
        var activeStatuses = new[]
        {
            PaymentRequestStatus.Pending,
            PaymentRequestStatus.Processing,
            PaymentRequestStatus.RequiresReview,
            PaymentRequestStatus.Unknown
        };

        return await context.PaymentRequests
            .Where(r => r.Bookingid == bookingId
                && r.Provider == PaymentRequestProvider.PayOS
                && r.Phase == phase
                && activeStatuses.Contains(r.Status))
            .OrderByDescending(r => r.Createdat)
            .FirstOrDefaultAsync(ct);
    }

    private async Task ReconcilePaymentRequestAsync(
        PaymentRequest request,
        CancellationToken ct)
    {
        if (request.Ordercode == null
            && string.IsNullOrWhiteSpace(request.Paymentlinkid))
        {
            await MarkPaymentRequestForReviewAsync(
                request.Bookingid!.Value,
                request,
                ct);
            return;
        }

        try
        {
            var link = !string.IsNullOrWhiteSpace(request.Paymentlinkid)
                ? await _payOS.PaymentRequests.GetAsync(request.Paymentlinkid)
                : await _payOS.PaymentRequests.GetAsync(request.Ordercode!.Value);
            var providerStatus = NormalizePaymentRequestStatus(
                link.Status.ToString());
            var resolvedPhase = ResolvePaymentRequestPhase(link.OrderCode);
            var providerPayload = JsonSerializer.Serialize(
                link,
                PaymentPayloadJsonOptions);
            var destinationBankName = request.Destinationbankname
                ??
                await ResolveDestinationBankNameAsync(
                    request.Destinationbankbin,
                    ct);

            bool shouldCancel;
            await using (var tx = await context.Database.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable,
                ct))
            {
                var booking = await bookingRepo.FindWithRelationsForUpdateAsync(
                    request.Bookingid!.Value,
                    ct);
                await context.PaymentRequests.Entry(request).ReloadAsync(ct);

                var bookingPayable = booking != null
                    && IsBookingPhaseStillPayable(booking, resolvedPhase);
                request.Ordercode = link.OrderCode;
                request.Paymentlinkid = NormalizeOptional(link.Id)
                    ?? request.Paymentlinkid;
                request.Amount = link.Amount;
                request.Phase = resolvedPhase;
                request.Providerpayload = providerPayload;
                request.Destinationbankname ??= destinationBankName;
                request.Updatedat =
                    MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;

                if (providerStatus == PaymentRequestStatus.Paid
                    || (PaymentRequestStatus.IsActive(request.Status)
                        && bookingPayable))
                {
                    request.Status = providerStatus;
                }
                else if (PaymentRequestStatus.IsActive(request.Status)
                    && !bookingPayable)
                {
                    request.Status = PaymentRequestStatus.RequiresReview;
                }

                shouldCancel = !bookingPayable
                    && providerStatus != PaymentRequestStatus.Paid
                    && request.Status is not (
                        PaymentRequestStatus.Cancelled
                        or PaymentRequestStatus.Expired);

                await context.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }

            if (shouldCancel)
            {
                await CancelIncompletePaymentRequestAsync(
                    request,
                    "Booking is no longer payable while reconciling its PayOS link.",
                    ct);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Could not reconcile PayOS payment request {PaymentRequestId}.",
                request.Paymentrequestid);
            await MarkPaymentRequestForReviewAsync(
                request.Bookingid!.Value,
                request,
                ct);
        }
    }

    private async Task CancelIncompletePaymentRequestAsync(
        PaymentRequest request,
        string reason,
        CancellationToken ct)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(request.Paymentlinkid))
            {
                await _payOS.PaymentRequests.CancelAsync(
                    request.Paymentlinkid,
                    reason);
            }
            else if (request.Ordercode.HasValue)
            {
                await _payOS.PaymentRequests.CancelAsync(
                    request.Ordercode.Value,
                    reason);
            }

            await using var tx = await context.Database.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable,
                ct);
            _ = await bookingRepo.FindWithRelationsForUpdateAsync(
                request.Bookingid!.Value,
                ct);
            await context.PaymentRequests.Entry(request).ReloadAsync(ct);
            if (request.Status != PaymentRequestStatus.Paid)
            {
                request.Status = PaymentRequestStatus.Superseded;
                request.Updatedat =
                    MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
                await context.SaveChangesAsync(ct);
            }
            await tx.CommitAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Failed cancelling incomplete PayOS request {PaymentRequestId}.",
                request.Paymentrequestid);
            await MarkPaymentRequestForReviewAsync(
                request.Bookingid!.Value,
                request,
                ct);
            throw new BookingException(
                BookingErrorCodes.InvalidBookingStatus,
                "Không thể thay thế link thanh toán cũ; cần đối soát thủ công.",
                409);
        }
    }

    private async Task SupersedeActivePayOSRequestsAsync(
        int bookingId,
        string phase,
        string reason,
        CancellationToken ct)
    {
        try
        {
            var activeStatuses = new[]
            {
                PaymentRequestStatus.Pending,
                PaymentRequestStatus.Processing,
                PaymentRequestStatus.RequiresReview,
                PaymentRequestStatus.Unknown
            };
            var requests = await context.PaymentRequests
                .Where(r => r.Bookingid == bookingId
                    && r.Provider == PaymentRequestProvider.PayOS
                    && r.Phase == phase
                    && activeStatuses.Contains(r.Status))
                .ToListAsync(ct);

            foreach (var request in requests)
            {
                var providerCancelled = false;
                var providerSnapshotVerified = false;
                Exception? cancellationException = null;
                Exception? reconciliationException = null;
                string? reconciledProviderStatus = null;
                PayOS.Models.V2.PaymentRequests.PaymentLink?
                    cancellationSnapshot = null;
                PayOS.Models.V2.PaymentRequests.PaymentLink?
                    providerSnapshot = null;
                try
                {
                    if (!string.IsNullOrWhiteSpace(
                            request.Paymentlinkid))
                    {
                        cancellationSnapshot =
                            await _payOS.PaymentRequests.CancelAsync(
                            request.Paymentlinkid,
                            reason);
                        providerCancelled = true;
                    }
                    else if (request.Ordercode.HasValue)
                    {
                        cancellationSnapshot =
                            await _payOS.PaymentRequests.CancelAsync(
                            request.Ordercode.Value,
                            reason);
                        providerCancelled = true;
                    }
                }
                catch (Exception ex)
                {
                    cancellationException = ex;
                    logger.LogWarning(
                        ex,
                        "Could not cancel PayOS request {PaymentRequestId} after alternate-channel payment.",
                        request.Paymentrequestid);
                }

                // Cancellation and the bank event may cross in flight. Always
                // perform an authoritative GET before declaring the request
                // terminal, even when no local transaction has been captured.
                try
                {
                    if (!string.IsNullOrWhiteSpace(request.Paymentlinkid))
                    {
                        providerSnapshot =
                            await _payOS.PaymentRequests.GetAsync(
                                request.Paymentlinkid);
                    }
                    else if (request.Ordercode.HasValue)
                    {
                        providerSnapshot =
                            await _payOS.PaymentRequests.GetAsync(
                                request.Ordercode.Value);
                    }

                    if (providerSnapshot != null)
                    {
                        providerSnapshotVerified = true;
                        reconciledProviderStatus =
                            NormalizePaymentRequestStatus(
                                providerSnapshot.Status.ToString());
                        providerCancelled |= reconciledProviderStatus is
                            PaymentRequestStatus.Cancelled
                            or PaymentRequestStatus.Expired;
                    }
                }
                catch (Exception ex)
                {
                    reconciliationException = ex;
                    logger.LogWarning(
                        ex,
                        "Could not verify PayOS request {PaymentRequestId} after alternate-channel payment.",
                        request.Paymentrequestid);
                }

                var evidence = providerSnapshot ?? cancellationSnapshot;
                if (evidence != null)
                {
                    try
                    {
                        var captures = PaymentTransactionCapture
                            .FromPayOSPaymentLink(evidence);
                        foreach (var capture in captures)
                        {
                            await ConfirmPaymentInternalAsync(
                                evidence.OrderCode,
                                capture.ObservedAmount ?? 0,
                                capture.GetProcessingKey(),
                                capture,
                                ct);
                        }

                        if (captures.Count == 0
                            && NormalizePaymentRequestStatus(
                                evidence.Status.ToString())
                                == PaymentRequestStatus.Paid)
                        {
                            // A PAID snapshot without transfer rows cannot be
                            // considered fully reconciled or terminal.
                            providerSnapshotVerified = false;
                            await MarkPaidRequestWithoutTransactionsAsync(
                                request,
                                ct);
                        }
                    }
                    catch (Exception ex)
                    {
                        reconciliationException ??= ex;
                        providerSnapshotVerified = false;
                        logger.LogWarning(
                            ex,
                            "Could not persist PayOS evidence for request {PaymentRequestId} after alternate-channel payment.",
                            request.Paymentrequestid);
                    }
                }

                // A webhook may have completed while the provider cancellation
                // was in flight. Serialize the outcome with booking confirmation
                // and never replace a freshly PAID request with SUPERSEDED.
                var refundedAny = false;
                Booking? lockedBooking = null;
                await using (var tx = await context.Database.BeginTransactionAsync(
                    System.Data.IsolationLevel.Serializable,
                    ct))
                {
                    lockedBooking = await bookingRepo
                        .FindWithRelationsForUpdateAsync(
                            bookingId,
                            ct);
                    await context.PaymentRequests.Entry(request).ReloadAsync(ct);

                    if (request.Status != PaymentRequestStatus.Paid)
                    {
                        var unsettledCaptures = await context
                        .PaymentTransactions
                        .Where(transaction =>
                            transaction.Paymentrequestid
                                == request.Paymentrequestid
                            && (transaction.Reconciliationstatus
                                    == PaymentReconciliationStatus.Partial
                                || transaction.Reconciliationstatus
                                    == PaymentReconciliationStatus.AmountMismatch))
                        .ToListAsync(ct);

                        if (lockedBooking != null
                            && !string.IsNullOrWhiteSpace(
                                lockedBooking.Parentid))
                        {
                            foreach (var transaction in unsettledCaptures)
                            {
                                var processingKey =
                                    PaymentRequestReconciliationPolicy
                                        .GetPersistedCaptureProcessingKey(
                                            transaction);
                                if (processingKey == null)
                                    continue;

                                var refunded =
                                    await RefundOrphanPaymentToWalletAsync(
                                        lockedBooking,
                                        transaction.Amount,
                                        $"payos-extra-refund:{processingKey}",
                                        ct);
                                refundedAny |= refunded;
                                transaction.Reconciliationstatus =
                                    PaymentReconciliationStatus.Unexpected;
                                AddPaymentReconciliationAlert(
                                    transaction,
                                    request,
                                    PaymentAlertType.UnexpectedTransaction,
                                    "PayOS capture was refunded because the booking phase was paid through another channel");
                            }
                        }

                        var hasUnsettledCaptures = unsettledCaptures.Any(
                            transaction => transaction.Reconciliationstatus
                                != PaymentReconciliationStatus.Unexpected);

                        var resolvedStatus =
                            PaymentRequestReconciliationPolicy
                                .ResolveAfterAlternatePayment(
                                    providerSnapshotVerified,
                                    reconciledProviderStatus,
                                    hasUnsettledCaptures);

                        if (resolvedStatus
                            == PaymentRequestStatus.RequiresReview)
                        {
                            context.Systemalerts.Add(new Systemalert
                            {
                                Type = PaymentAlertType.CancellationFailed,
                                Severity = "High",
                                Message =
                                    "A PayOS link could not be cancelled or verified after the booking was paid through another channel.",
                                Metadata = JsonSerializer.Serialize(new
                                {
                                    paymentRequestId =
                                        request.Paymentrequestid,
                                    request.Bookingid!.Value,
                                    request.Ordercode,
                                    request.Paymentlinkid,
                                    cancellationFailed =
                                        cancellationException != null,
                                    cancellationSucceeded =
                                        providerCancelled,
                                    reconciliationFailed =
                                        reconciliationException != null,
                                    providerSnapshotVerified,
                                    providerStatus =
                                        reconciledProviderStatus,
                                    reason
                                }),
                                Resolved = false,
                                Createdat =
                                    MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow
                            });
                        }

                        // SUPERSEDED is terminal after an authoritative provider
                        // snapshot confirms PAID/CANCELLED/EXPIRED and every
                        // observed transfer has a terminal disposition.
                        request.Status = resolvedStatus;
                        request.Updatedat =
                            MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
                        await context.SaveChangesAsync(ct);
                    }

                    await tx.CommitAsync(ct);
                }

                if (refundedAny && lockedBooking != null)
                {
                    try
                    {
                        await SendRefundNotificationAsync(lockedBooking);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(
                            ex,
                            "Could not send alternate-channel PayOS refund notification for request {PaymentRequestId}.",
                            request.Paymentrequestid);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // The booking payment has already committed. Never report it as
            // failed solely because best-effort link cleanup could not persist.
            logger.LogError(
                ex,
                "Failed superseding PayOS requests for booking {BookingId}, phase {Phase}.",
                bookingId,
                phase);
        }
    }

    private PaymentInfoResponse BuildPaymentInfoResponse(
        Booking booking,
        PaymentRequest request,
        Wallet? wallet)
    {
        var amount = request.Amount ?? 0;
        return new PaymentInfoResponse
        {
            BookingId = booking.Bookingid,
            PaymentLinkId = request.Paymentlinkid ?? string.Empty,
            PaymentCode = request.Ordercode?.ToString() ?? string.Empty,
            Amount = amount,
            Currency = request.Currency,
            CheckoutUrl = request.Checkouturl ?? string.Empty,
            QrCode = request.Qrcode ?? string.Empty,
            AccountNumber = request.Displayaccountnumber ?? string.Empty,
            AccountName = request.Displayaccountname ?? string.Empty,
            Bin = request.Destinationbankbin ?? string.Empty,
            Description = request.Description ?? string.Empty,
            ExpiredAt = request.Expiresat,
            Status = request.Status,
            CanPayWithWallet = (wallet?.Balance ?? 0) >= amount,
            WalletBalance = wallet?.Balance ?? 0,
            PaymentPhase = request.Phase,
            TotalAmount = booking.Finalprice ?? 0,
            DepositAmount = booking.Depositamount ?? 0,
            RemainingAmount = booking.Remainingamount ?? 0,
            IsDepositPaid = booking.Depositpaidat != null,
            IsRemainingPaid = booking.Remainingpaidat != null
        };
    }

    private static string ResolvePaymentRequestPhase(long orderCode)
    {
        if (OrderCodeHelper.IsBookingOrderCode(orderCode))
            return PaymentRequestPhase.Deposit;
        if (OrderCodeHelper.IsRemainingOrderCode(orderCode))
            return PaymentRequestPhase.Remaining;
        return PaymentRequestPhase.LegacyUnknown;
    }

    private static string NormalizePaymentRequestStatus(string? status)
    {
        var normalized = status?.Trim().ToUpperInvariant();
        return normalized switch
        {
            PayOSLinkStatus.Pending => PaymentRequestStatus.Pending,
            PayOSLinkStatus.Processing => PaymentRequestStatus.Processing,
            PayOSLinkStatus.Paid => PaymentRequestStatus.Paid,
            PayOSLinkStatus.Cancelled => PaymentRequestStatus.Cancelled,
            PayOSLinkStatus.Expired => PaymentRequestStatus.Expired,
            _ => PaymentRequestStatus.RequiresReview
        };
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private async Task<string?> ResolveDestinationBankNameAsync(
        string? bin,
        CancellationToken ct)
    {
        var normalizedBin = NormalizeOptional(bin);
        if (normalizedBin == null)
            return null;

        try
        {
            var banks = await bankListService.GetBankListAsync(ct);
            var bank = banks.FirstOrDefault(b =>
                string.Equals(
                    b.Bin,
                    normalizedBin,
                    StringComparison.Ordinal));
            return NormalizeOptional(bank?.ShortName)
                ?? NormalizeOptional(bank?.FullName)
                ?? NormalizeOptional(bank?.Code);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Could not resolve destination bank name for BIN {Bin}.",
                normalizedBin);
            return null;
        }
    }
}
