using System.Linq.Expressions;
using MV.DomainLayer.Constants;
using MV.DomainLayer.Entities;

namespace MV.ApplicationLayer.Helpers;

/// <summary>
/// Shared rules for keeping PayOS requests visible to reconciliation until all
/// money already observed at the provider has reached a terminal disposition.
/// </summary>
public static class PaymentRequestReconciliationPolicy
{
    public static Expression<Func<PaymentRequest, bool>> BuildCandidatePredicate(
        DateTime creationGraceCutoff)
        => request =>
            request.Provider == PaymentRequestProvider.PayOS
            && !(request.Status == PaymentRequestStatus.Pending
                && request.Paymentlinkid == null
                && request.Createdat > creationGraceCutoff)
            && (request.Phase == PaymentRequestPhase.LegacyUnknown
                || request.Ordercode == null
                || request.Status == PaymentRequestStatus.Pending
                || request.Status == PaymentRequestStatus.Processing
                || request.Status == PaymentRequestStatus.RequiresReview
                || request.Status == PaymentRequestStatus.Unknown
                || (request.Status == PaymentRequestStatus.Paid
                    && !request.Paymenttransactions.Any(transaction =>
                        transaction.Reconciliationstatus
                            == PaymentReconciliationStatus.Matched))
                // A successful provider cancellation may mark a request as
                // superseded after the booking was paid via wallet/manual. Any
                // transfer observed before that cancellation still has to be
                // polled once more and refunded; never hide it by status alone.
                || request.Paymenttransactions.Any(transaction =>
                    transaction.Reconciliationstatus
                        == PaymentReconciliationStatus.Partial
                    || transaction.Reconciliationstatus
                        == PaymentReconciliationStatus.AmountMismatch));

    public static string ResolveAfterAlternatePayment(
        bool providerSnapshotVerified,
        string? providerStatus,
        bool hasUnsettledCaptures)
        => providerSnapshotVerified
            && providerStatus is PaymentRequestStatus.Paid
                or PaymentRequestStatus.Cancelled
                or PaymentRequestStatus.Expired
            && !hasUnsettledCaptures
            ? PaymentRequestStatus.Superseded
            : PaymentRequestStatus.RequiresReview;

    /// <summary>
    /// Uses the immutable database row identity for a persisted capture, so an
    /// alternate-channel refund remains idempotent even when provider metadata
    /// is enriched later or two real transfers share an observation fingerprint.
    /// </summary>
    public static string? GetPersistedCaptureProcessingKey(
        PaymentTransaction transaction)
    {
        return PaymentTransactionCapture.GetStableStoredProcessingKey(
            transaction);
    }
}
