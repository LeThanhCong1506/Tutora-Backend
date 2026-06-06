namespace MV.DomainLayer.Constants;

public static class BankVerificationConstants
{
    public const int MaxLookupsPerDay = 10;
    public const int MaxVerificationAttempts = 5;
    public const int MinAccountNumberLength = 8;
    public const int MaxAccountNumberLength = 19;
    public const string VerificationCodePrefix = "AGORA"; // Cần đổi sang TUTORA
    public const string RateLimitKeyPrefix = "bank-lookup";
    public const string BankListCacheKey = "bank-list";
    public const int BankListCacheDurationHours = 24;
    public const int BankChangeCooldownHours = 24;
    public const int MaxBankChangesPerMonth = 2;
    public const int MaskStartDigits = 3;
    public const int MaskEndDigits = 3;

    // Micro-deposit constants (M4-T2)
    public const int MicroDepositAmount = 1000;
    public const int VerificationCodeLength = 6;
    public const int VerificationExpiryHours = 24;
    public const int MaxRequestsPerDay = 3;

    public static class Statuses
    {
        public const string None = "none";
        public const string PendingDeposit = "pending_deposit";
        public const string ReadyToConfirm = "ready_to_confirm";
        public const string Verified = "verified";
        public const string Failed = "failed";
    }

    public static class ErrorCodes
    {
        public const string InvalidAccountNumber = "INVALID_ACCOUNT_NUMBER";
        public const string InvalidBankCode = "INVALID_BANK_CODE";
        public const string AccountNotFound = "ACCOUNT_NOT_FOUND";
        public const string RateLimitExceeded = "RATE_LIMIT_EXCEEDED";
        public const string VerificationLocked = "VERIFICATION_LOCKED";
        public const string BankNotVerified = "BANK_NOT_VERIFIED";
        public const string BankChangeCooldown = "BANK_CHANGE_COOLDOWN";
        public const string MaxBankChangesExceeded = "MAX_BANK_CHANGES_EXCEEDED";
        public const string ApiTimeout = "API_TIMEOUT";
        public const string ApiError = "API_ERROR";
        public const string BankMaintenance = "BANK_MAINTENANCE";
        public const string NameMismatch = "NAME_MISMATCH";
        public const string DepositPending = "DEPOSIT_PENDING";
        public const string DepositFailed = "DEPOSIT_FAILED";
        public const string VerificationExpired = "VERIFICATION_EXPIRED";
        public const string AlreadyVerified = "ALREADY_VERIFIED";
        public const string InsufficientWalletBalance = "INSUFFICIENT_WALLET_BALANCE";
    }

    public static class ErrorMessages
    {
        public const string InvalidAccountNumber = "Invalid account number. Please enter 8-19 digits.";
        public const string InvalidBankCode = "Invalid bank code.";
        public const string AccountNotFound = "Account number does not exist or is incorrect.";
        public const string RateLimitExceeded = "You have exceeded the maximum number of lookups per day. Please try again tomorrow.";
        public const string VerificationLocked = "Your account has been locked due to too many failed attempts. Please contact support.";
        public const string BankNotVerified = "Please verify your bank account before making transactions.";
        public const string BankChangeCooldown = "You must wait 24 hours after changing bank information before withdrawing funds.";
        public const string MaxBankChangesExceeded = "You have changed bank information too many times this month.";
        public const string ApiTimeout = "Unable to lookup bank information at this time. Please try again later.";
        public const string ApiError = "An error occurred while looking up bank information. Please try again.";
        public const string BankMaintenance = "Bank system is under maintenance. Please try again later.";
        public const string NameMismatch = "Account holder name does not match. Please check again.";
        public const string DepositPending = "Verification transaction is being processed. Please wait a moment.";
        public const string DepositFailed = "Verification transaction failed. Please try again.";
        public const string VerificationExpired = "Verification code has expired. Please request a new code.";
        public const string AlreadyVerified = "Bank account has already been verified.";
        public const string InsufficientWalletBalance = "Insufficient wallet balance to perform verification. Please top up your wallet.";
    }
}
