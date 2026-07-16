namespace MV.DomainLayer.Constants;

public static class PaymentTransactionChannel
{
    public const string PayOS = "PayOS";
    public const string Manual = "Manual";
}

public static class PaymentTransactionDirection
{
    public const string Inbound = "Inbound";
    public const string Outbound = "Outbound";
}

public static class PaymentTransactionPurpose
{
    public const string BookingDeposit = "BookingDeposit";
    public const string BookingRemaining = "BookingRemaining";
    public const string WalletTopup = "WalletTopup";
    public const string Withdrawal = "Withdrawal";
}

public static class PaymentTransactionStatus
{
    public const string Succeeded = "Succeeded";
}
