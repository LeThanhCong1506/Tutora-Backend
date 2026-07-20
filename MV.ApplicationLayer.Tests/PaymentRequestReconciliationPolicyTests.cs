using MV.ApplicationLayer.Helpers;
using MV.DomainLayer.Constants;
using MV.DomainLayer.Entities;
using Xunit;

namespace MV.ApplicationLayer.Tests;

public class PaymentRequestReconciliationPolicyTests
{
    [Fact]
    public void SupersededRequest_WithPartialPayOSCapture_RemainsAReconciliationCandidate()
    {
        var request = CreateSupersededRequest(
            PaymentReconciliationStatus.Partial);

        var predicate = PaymentRequestReconciliationPolicy
            .BuildCandidatePredicate(DateTime.UtcNow.AddMinutes(-2))
            .Compile();

        Assert.True(predicate(request));
    }

    [Fact]
    public void SupersededRequest_WithOnlyTerminalUnexpectedCapture_IsNotPolledForever()
    {
        var request = CreateSupersededRequest(
            PaymentReconciliationStatus.Unexpected);

        var predicate = PaymentRequestReconciliationPolicy
            .BuildCandidatePredicate(DateTime.UtcNow.AddMinutes(-2))
            .Compile();

        Assert.False(predicate(request));
    }

    [Theory]
    [InlineData(true, PaymentRequestStatus.Paid, false, PaymentRequestStatus.Superseded)]
    [InlineData(true, PaymentRequestStatus.Cancelled, false, PaymentRequestStatus.Superseded)]
    [InlineData(true, PaymentRequestStatus.Expired, false, PaymentRequestStatus.Superseded)]
    [InlineData(true, PaymentRequestStatus.Paid, true, PaymentRequestStatus.RequiresReview)]
    [InlineData(true, PaymentRequestStatus.Pending, false, PaymentRequestStatus.RequiresReview)]
    [InlineData(false, PaymentRequestStatus.Paid, false, PaymentRequestStatus.RequiresReview)]
    public void AlternatePaymentStatus_TerminatesOnlyVerifiedSettledProviderSnapshots(
        bool providerSnapshotVerified,
        string providerStatus,
        bool hasUnsettledCaptures,
        string expectedStatus)
    {
        Assert.Equal(
            expectedStatus,
            PaymentRequestReconciliationPolicy.ResolveAfterAlternatePayment(
                providerSnapshotVerified,
                providerStatus,
                hasUnsettledCaptures));
    }

    [Theory]
    [InlineData(41, "FT260715123456", "fingerprint", "payment-transaction-41")]
    [InlineData(42, null, "fingerprint", "payment-transaction-42")]
    [InlineData(0, "FT260715123456", "fingerprint", null)]
    public void PersistedCaptureKey_UsesStablePaymentTransactionRowIdentity(
        int paymentTransactionId,
        string? providerTransactionId,
        string? captureFingerprint,
        string? expected)
    {
        var transaction = new PaymentTransaction
        {
            Paymenttransactionid = paymentTransactionId,
            Providertransactionid = providerTransactionId,
            Capturefingerprint = captureFingerprint
        };

        Assert.Equal(
            expected,
            PaymentRequestReconciliationPolicy
                .GetPersistedCaptureProcessingKey(transaction));
    }

    private static PaymentRequest CreateSupersededRequest(
        string reconciliationStatus)
        => new()
        {
            Provider = PaymentRequestProvider.PayOS,
            Phase = PaymentRequestPhase.Deposit,
            Status = PaymentRequestStatus.Superseded,
            Ordercode = 123_456,
            Paymentlinkid = "payos-link-id",
            Createdat = DateTime.UtcNow.AddHours(-1),
            Paymenttransactions =
            {
                new PaymentTransaction
                {
                    Reconciliationstatus = reconciliationStatus
                }
            }
        };
}
