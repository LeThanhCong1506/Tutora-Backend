using System.Security.Cryptography;
using System.Text;
using MV.DomainLayer.Constants;
using MV.DomainLayer.Entities;

namespace MV.ApplicationLayer.Helpers;

/// <summary>
/// Creates the payment audit row for a booking phase paid from the internal wallet.
/// The wallet ledger remains the source of truth for balance changes; this row records
/// which payment method completed the booking phase.
/// </summary>
public static class WalletPaymentTransactionFactory
{
    public static PaymentTransaction Create(
        int bookingId,
        string userId,
        bool isDepositPhase,
        decimal amount,
        DateTime paidAtUtc)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bookingId);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amount);

        var purpose = isDepositPhase
            ? PaymentTransactionPurpose.BookingDeposit
            : PaymentTransactionPurpose.BookingRemaining;
        var description = isDepositPhase
            ? $"Wallet deposit payment for booking #{bookingId}"
            : $"Wallet remaining payment for booking #{bookingId}";
        var fingerprintSource =
            $"{PaymentTransactionMethod.Wallet}|{bookingId}|{purpose}";
        var fingerprint = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(fingerprintSource)))
            .ToLowerInvariant();

        return new PaymentTransaction
        {
            Userid = userId.Trim(),
            Paymentmethod = PaymentTransactionMethod.Wallet,
            Direction = PaymentTransactionDirection.Internal,
            Purpose = purpose,
            Status = PaymentTransactionStatus.Succeeded,
            Amount = amount,
            Currency = Currency.Vnd,
            Bookingid = bookingId,
            Paymentrequestid = null,
            Description = description,
            Paidat = paidAtUtc,
            Createdat = paidAtUtc,
            Capturesource = PaymentCaptureSource.InternalWallet,
            Reconciliationstatus = PaymentReconciliationStatus.NotApplicable,
            Capturefingerprint = fingerprint,
            Note = "Paid using internal wallet balance."
        };
    }
}
