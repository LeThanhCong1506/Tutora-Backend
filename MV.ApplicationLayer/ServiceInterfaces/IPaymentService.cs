using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;

namespace MV.ApplicationLayer.ServiceInterfaces;

public interface IPaymentService
{
    /// <summary>
    /// Get payment info for a booking's current unpaid phase.
    /// </summary>
    Task<PaymentInfoResponse> GetPaymentInfoAsync(int bookingId, string userId);

    /// <summary>
    /// Lightweight payment summary (amounts + wallet balance) for the current unpaid
    /// phase. Does NOT create a PayOS request/link, so paying by wallet leaves no
    /// PayOS artifact. The link is created lazily via <see cref="GetPaymentInfoAsync"/>
    /// only when the parent picks bank transfer.
    /// </summary>
    Task<PaymentSummaryResponse> GetPaymentSummaryAsync(int bookingId, string userId);

    /// <summary>
    /// Create a PayOS top-up for exactly the wallet shortfall of the booking's current payment phase.
    /// The amount is calculated server-side; standalone wallet top-ups are not supported.
    /// </summary>
    Task<TopupResponse> CreateBookingShortfallTopupAsync(int bookingId, string userId, CancellationToken ct = default);

    /// <summary>
    /// Apply a completed booking-scoped shortfall top-up to the same booking and payment phase.
    /// </summary>
    Task ApplyBookingShortfallTopupAsync(int bookingId, long orderCode, string userId, CancellationToken ct = default);

    /// <summary>
    /// Process an inbound webhook callback; updates booking/payment status.
    /// </summary>
    Task ProcessWebhookAsync(PaymentWebhookRequest request, string rawPayload, CancellationToken ct = default);

    /// <summary>
    /// Persist a verified PayOS webhook that cannot be routed to a known
    /// booking/top-up so the money movement is never silently discarded.
    /// </summary>
    Task RecordUnmatchedPayOSWebhookAsync(PaymentWebhookRequest request, string rawPayload, CancellationToken ct = default);

    /// <summary>
    /// Poll and apply one persisted PayOS request. Used by the background
    /// reconciler when the original webhook was missed.
    /// </summary>
    Task ReconcilePaymentRequestByIdAsync(int paymentRequestId, CancellationToken ct = default);

    /// <summary>
    /// Verify the HMAC-SHA256 signature on a raw webhook payload.
    /// </summary>
    Task<bool> VerifyWebhookSignatureAsync(string payload, string signature);

    /// <summary>
    /// Admin or an authorized Staff member manually confirms an offline payment for a booking.
    /// </summary>
    Task ConfirmPaymentManuallyAsync(int bookingId, AdminConfirmPaymentRequest request, string? actorUserId = null, CancellationToken ct = default);

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
}
