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
    // ─── Payment Info (PayOS link creation) ──────────────────────────────

    public async Task<PaymentInfoResponse> GetPaymentInfoAsync(int bookingId, string userId)
    {
        var booking = await bookingRepo.FindForPaymentByUserAsync(bookingId, userId)
            ?? throw new BookingException(BookingErrorCodes.BookingNotFound, ApiMessages.BookingNotFound, 404);

        if (booking.Paymentstatus == Escrowed || booking.Status == BookingStatus.Paid)
            throw new BookingException(BookingErrorCodes.BookingAlreadyPaid, "Booking đã được thanh toán rồi", 409);

        EnsureDepositAmountsCalculated(booking);

        bool isDepositPhase = booking.Depositpaidat == null;
        bool isRemainingPhase = booking.Depositpaidat != null && booking.Remainingpaidat == null;

        if (isDepositPhase)
            return await GetDepositPaymentInfoAsync(booking, bookingId, userId);
        else if (isRemainingPhase)
            return await GetRemainingPaymentInfoAsync(booking, bookingId, userId);
        else
            throw new BookingException(BookingErrorCodes.BookingAlreadyPaid, "Booking đã được thanh toán rồi", 409);
    }

    private async Task<PaymentInfoResponse> GetDepositPaymentInfoAsync(Booking booking, int bookingId, string userId)
    {
        if (booking.Status != BookingStatus.Accepted && booking.Status != BookingStatus.PendingPayment)
            throw new BookingException(BookingErrorCodes.InvalidBookingStatus, "Booking chưa ở trạng thái sẵn sàng để thanh toán", 409);

        if (booking.Paymentdueat <= MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow)
            throw new BookingException(BookingErrorCodes.BookingExpired, "Booking đã quá hạn thanh toán", 409);

        var depositAmount = (int)(booking.Depositamount ?? 0);

        // Reuse existing PayOS link if still active
        if (booking.Status == BookingStatus.PendingPayment && !string.IsNullOrEmpty(booking.Paymentcode))
        {
            var walletForExisting = await walletRepo.GetByUserIdAsNoTrackingAsync(userId);
            try
            {
                var existingLink = await _payOS.PaymentRequests.GetAsync(booking.Paymentcode);
                var linkStatus = existingLink.Status.ToString().ToUpper();
                if (linkStatus == PayOSLinkStatus.Pending || linkStatus == PayOSLinkStatus.Processing)
                    return BuildPaymentInfoResponse(booking, bookingId, existingLink,
                        existingLink.OrderCode, depositAmount, walletForExisting, PaymentPhase.Deposit);

                logger.LogInformation("Existing PayOS link for booking {BookingId} has status {Status}, creating new one",
                    bookingId, linkStatus);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to get existing PayOS link for booking {BookingId}, creating new one", bookingId);
            }
        }

        // Recalculate fees if needed
        if (booking.Finalprice == null || booking.Finalprice == 0)
        {
            var baseAmount = Math.Max((booking.Totalamount ?? 0) - (booking.Discountapplied ?? 0), 0);
            var fees = BookingFeeCalculator.Calculate(baseAmount);
            booking.Finalprice = fees.FinalPrice;
            booking.Parentfee = fees.ParentFee;
            booking.Platformfee = fees.PlatformFee;
            booking.Tutorfee = fees.TutorReceivable;
            EnsureDepositAmountsCalculated(booking);
            depositAmount = (int)(booking.Depositamount ?? 0);
        }

        var orderCode = OrderCodeHelper.GenerateBookingOrderCode(bookingId);
        booking.Status = BookingStatus.PendingPayment;
        booking.Updatedat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;

        var wallet = await walletRepo.GetByUserIdAsNoTrackingAsync(userId);
        var expiredAt = booking.Paymentdueat.HasValue
            ? (int)((DateTimeOffset)booking.Paymentdueat.Value).ToUnixTimeSeconds()
            : (int)DateTimeOffset.UtcNow.AddHours(24).ToUnixTimeSeconds();

        try
        {
            var paymentLink = await _linkFactory.CreatePaymentLink(orderCode, depositAmount, $"Coc Booking #{bookingId}", expiredAt);
            booking.Paymentcode = paymentLink.PaymentLinkId;
            await context.SaveChangesAsync();

            logger.LogInformation("Created PayOS deposit link {Id} for booking {BookingId}, amount {Amount}",
                paymentLink.PaymentLinkId, bookingId, depositAmount);

            return BuildPaymentInfoResponse(booking, bookingId, paymentLink, orderCode, depositAmount, wallet, PaymentPhase.Deposit);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create PayOS link for booking {BookingId}", bookingId);
            throw new BookingException(BookingErrorCodes.InvalidInput, "Tạo link thanh toán thất bại: " + ex.Message, 500);
        }
    }

    private async Task<PaymentInfoResponse> GetRemainingPaymentInfoAsync(Booking booking, int bookingId, string userId)
    {
        if (booking.Status != BookingStatus.DepositPaid && booking.Status != BookingStatus.PendingRemainingPayment
            && booking.Status != BookingStatus.Ongoing)
            throw new BookingException(BookingErrorCodes.InvalidBookingStatus, "Booking chưa ở trạng thái sẵn sàng để thanh toán phần còn lại", 409);

        var remainingAmount = (int)(booking.Remainingamount ?? 0);
        var wallet = await walletRepo.GetByUserIdAsNoTrackingAsync(userId);
        var expiredAt = booking.Paymentdueat.HasValue
            ? (int)((DateTimeOffset)booking.Paymentdueat.Value).ToUnixTimeSeconds()
            : (int)DateTimeOffset.UtcNow.AddHours(48).ToUnixTimeSeconds();

        // Reuse existing link if still active
        if (!string.IsNullOrEmpty(booking.Paymentcode))
        {
            try
            {
                var existingLink = await _payOS.PaymentRequests.GetAsync(booking.Paymentcode);
                var linkStatus = existingLink.Status.ToString().ToUpper();
                if (linkStatus == PayOSLinkStatus.Pending || linkStatus == PayOSLinkStatus.Processing)
                    return BuildPaymentInfoResponse(booking, bookingId, existingLink,
                        existingLink.OrderCode, remainingAmount, wallet, PaymentPhase.Remaining);

                logger.LogInformation("Existing remaining PayOS link for booking {BookingId} has status {Status}, creating new one",
                    bookingId, linkStatus);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to get existing remaining PayOS link for booking {BookingId}, creating new one", bookingId);
            }
        }

        var orderCode = OrderCodeHelper.GenerateRemainingOrderCode(bookingId);
        try
        {
            var paymentLink = await _linkFactory.CreatePaymentLink(orderCode, remainingAmount, $"Tra not Booking #{bookingId}", expiredAt);
            booking.Paymentcode = paymentLink.PaymentLinkId;
            await context.SaveChangesAsync();

            logger.LogInformation("Created PayOS remaining link {Id} for booking {BookingId}, amount {Amount}",
                paymentLink.PaymentLinkId, bookingId, remainingAmount);

            return BuildPaymentInfoResponse(booking, bookingId, paymentLink, orderCode, remainingAmount, wallet, PaymentPhase.Remaining);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create PayOS remaining link for booking {BookingId}", bookingId);
            throw new BookingException(BookingErrorCodes.InvalidInput, "Tạo link thanh toán thất bại: " + ex.Message, 500);
        }
    }

    private PaymentInfoResponse BuildPaymentInfoResponse(Booking booking, int bookingId,
        dynamic paymentLink, long orderCode, int amount, Wallet? wallet, string phase)
    {
        return new PaymentInfoResponse
        {
            BookingId = bookingId,
            PaymentLinkId = paymentLink.PaymentLinkId,
            PaymentCode = orderCode.ToString(),
            Amount = amount,
            Currency = paymentLink.Currency ?? Currency.Vnd,
            CheckoutUrl = paymentLink.CheckoutUrl,
            QrCode = paymentLink.QrCode,
            AccountNumber = paymentLink.AccountNumber ?? "",
            AccountName = paymentLink.AccountName ?? "",
            Bin = paymentLink.Bin ?? "",
            Description = paymentLink.Description ?? "",
            ExpiredAt = (long)paymentLink.ExpiredAt > 0
                ? DateTimeOffset.FromUnixTimeSeconds((long)paymentLink.ExpiredAt).UtcDateTime : null,
            Status = paymentLink.Status.ToString(),
            CanPayWithWallet = (wallet?.Balance ?? 0) >= amount,
            WalletBalance = wallet?.Balance ?? 0,
            PaymentPhase = phase,
            TotalAmount = booking.Finalprice ?? 0,
            DepositAmount = booking.Depositamount ?? 0,
            RemainingAmount = booking.Remainingamount ?? 0,
            IsDepositPaid = booking.Depositpaidat != null,
            IsRemainingPaid = booking.Remainingpaidat != null
        };
    }
}
