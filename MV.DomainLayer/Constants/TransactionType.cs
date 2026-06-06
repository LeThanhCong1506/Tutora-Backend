namespace MV.DomainLayer.Constants;

public static class TransactionType
{
    public const string Deposit = "Deposit";
    public const string Payment = "Payment";
    public const string EscrowCredit = "EscrowCredit";
    public const string EscrowRelease = "EscrowRelease";
    public const string Withdrawal = "Withdrawal";
    public const string Refund = "Refund";
    public const string DepositPayment = "DepositPayment";
    public const string RemainingPayment = "RemainingPayment";
    public const string BankVerification = "BankVerification";
}
