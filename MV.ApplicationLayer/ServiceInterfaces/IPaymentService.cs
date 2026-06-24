using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;

namespace MV.ApplicationLayer.ServiceInterfaces;

public interface IPaymentService
{
    /// <summary>
    /// Get ZaloPay QR / payment URL info for a booking's current unpaid phase.
    /// </summary>
    Task<PaymentInfoResponse> GetPaymentInfoAsync(int bookingId, string userId);

    /// <summary>
    /// Process an inbound ZaloPay webhook callback; updates booking/payment status.
    /// </summary>
    Task ProcessWebhookAsync(PaymentWebhookRequest request, CancellationToken ct = default);

    /// <summary>
    /// Verify the HMAC-SHA256 signature on a raw ZaloPay webhook payload.
    /// </summary>
    Task<bool> VerifyWebhookSignatureAsync(string payload, string signature);

    /// <summary>
    /// Admin manually confirms a payment for a booking (offline / cash flow).
    /// </summary>
    Task ConfirmPaymentByAdminAsync(int bookingId, AdminConfirmPaymentRequest request, CancellationToken ct = default);

    /// <summary>
    /// Test helper: admin confirms the current unpaid booking phase without manually passing the amount.
    /// </summary>
    Task<TestConfirmBookingPaymentResponse> ConfirmCurrentBookingPaymentForTestAsync(int bookingId, string? transactionId = null, CancellationToken ct = default);

    /// <summary>
    /// Deduct the booking amount from the user's in-app wallet balance.
    /// </summary>
    Task PayWithWalletAsync(int bookingId, string userId, CancellationToken ct = default);

    /// <summary>
    /// Tutor wallet summary: total earned, withdrawn, pending settlement.
    /// </summary>
    Task<WalletSummaryResponse> GetTutorWalletSummaryAsync(string tutorId);

    /// <summary>
    /// Snapshot of payment status for a booking — includes deposit and remaining-balance phases.
    /// </summary>
    Task<PaymentStatusResponse> GetPaymentStatusAsync(int bookingId, string userId);

    /// <summary>
    /// Lightweight booking info returned to the ZaloPay payment page before order creation.
    /// Returns <c>null</c> if the booking does not belong to the user or is not payable.
    /// </summary>
    Task<ZaloPayBookingInfoResponse?> GetBookingForZaloPayAsync(int bookingId, string userId);
}
