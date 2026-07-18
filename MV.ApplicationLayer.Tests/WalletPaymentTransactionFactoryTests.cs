using MV.ApplicationLayer.Helpers;
using MV.DomainLayer.Constants;
using Xunit;

namespace MV.ApplicationLayer.Tests;

public class WalletPaymentTransactionFactoryTests
{
    [Theory]
    [InlineData(true, PaymentTransactionPurpose.BookingDeposit)]
    [InlineData(false, PaymentTransactionPurpose.BookingRemaining)]
    public void Create_RecordsInternalWalletPaymentWithoutPayOSRequest(
        bool isDepositPhase,
        string expectedPurpose)
    {
        var paidAt = new DateTime(2026, 7, 17, 8, 30, 0, DateTimeKind.Utc);

        var transaction = WalletPaymentTransactionFactory.Create(
            139,
            "parent-id",
            isDepositPhase,
            157_500m,
            paidAt);

        Assert.Equal("parent-id", transaction.Userid);
        Assert.Equal(139, transaction.Bookingid);
        Assert.Equal(PaymentTransactionMethod.Wallet, transaction.Paymentmethod);
        Assert.Equal(PaymentTransactionDirection.Internal, transaction.Direction);
        Assert.Equal(expectedPurpose, transaction.Purpose);
        Assert.Equal(PaymentTransactionStatus.Succeeded, transaction.Status);
        Assert.Equal(157_500m, transaction.Amount);
        Assert.Equal(Currency.Vnd, transaction.Currency);
        Assert.Equal(PaymentCaptureSource.InternalWallet, transaction.Capturesource);
        Assert.Equal(PaymentReconciliationStatus.NotApplicable, transaction.Reconciliationstatus);
        Assert.Equal(paidAt, transaction.Paidat);
        Assert.Null(transaction.Paymentrequestid);
        Assert.Null(transaction.Paymentlinkid);
        Assert.Null(transaction.Ordercode);
        Assert.Null(transaction.Providertransactionid);
        Assert.Null(transaction.Providerpayload);
        Assert.Null(transaction.Webhookpayload);
        Assert.NotNull(transaction.Capturefingerprint);
        Assert.Equal(64, transaction.Capturefingerprint!.Length);
    }

    [Fact]
    public void Create_UsesStableFingerprintForTheSameBookingPhase()
    {
        var first = WalletPaymentTransactionFactory.Create(
            139,
            "parent-id",
            true,
            157_500m,
            new DateTime(2026, 7, 17, 8, 30, 0, DateTimeKind.Utc));
        var retry = WalletPaymentTransactionFactory.Create(
            139,
            "parent-id",
            true,
            157_500m,
            new DateTime(2026, 7, 17, 8, 31, 0, DateTimeKind.Utc));
        var remaining = WalletPaymentTransactionFactory.Create(
            139,
            "parent-id",
            false,
            630_000m,
            new DateTime(2026, 7, 17, 8, 32, 0, DateTimeKind.Utc));

        Assert.Equal(first.Capturefingerprint, retry.Capturefingerprint);
        Assert.NotEqual(first.Capturefingerprint, remaining.Capturefingerprint);
    }
}
