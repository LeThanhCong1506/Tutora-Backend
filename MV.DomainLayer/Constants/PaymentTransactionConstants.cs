namespace MV.DomainLayer.Constants;

public static class PaymentTransactionMethod
{
    public const string PayOS = "PayOS";
    public const string Manual = "Manual";
    public const string Wallet = "Wallet";
}

public static class PaymentTransactionDirection
{
    public const string Inbound = "Inbound";
    public const string Outbound = "Outbound";
    public const string Internal = "Internal";
}

public static class PaymentTransactionPurpose
{
    public const string BookingDeposit = "BookingDeposit";
    public const string BookingRemaining = "BookingRemaining";
    public const string WalletTopup = "WalletTopup";
    public const string Withdrawal = "Withdrawal";
    public const string UnmatchedPayOS = "UnmatchedPayOS";

    /// <summary>Mua gói AI credit (Homework Helper). Được tính vào doanh thu nền tảng.</summary>
    public const string AiCreditPurchase = "AiCreditPurchase";
}

public static class PaymentTransactionStatus
{
    public const string Pending = "Pending";
    public const string Succeeded = "Succeeded";
}
