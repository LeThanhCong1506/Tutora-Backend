namespace MV.DomainLayer.Constants;

public static class WalletErrorCodes
{
    public const string InvalidAmount = "INVALID_AMOUNT";
    public const string TopupNotFound = "TOPUP_NOT_FOUND";
    public const string AmountMismatch = "AMOUNT_MISMATCH";
    public const string DuplicateTransaction = "DUPLICATE_TRANSACTION";
    public const string WalletNotFound = "WALLET_NOT_FOUND";
    public const string InsufficientBalance = "INSUFFICIENT_BALANCE";
    public const string InsufficientBalanceForVerification = "INSUFFICIENT_BALANCE_FOR_VERIFICATION";
    public const string MinimumTopupRequired = "MINIMUM_TOPUP_REQUIRED";
}
