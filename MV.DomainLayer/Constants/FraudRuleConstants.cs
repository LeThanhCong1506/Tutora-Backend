namespace MV.DomainLayer.Constants;

public static class FraudRuleConstants
{
    public static class RuleNames
    {
        public const string VelocityCheck = "VELOCITY_CHECK";
        public const string AmountLimits = "AMOUNT_LIMITS";
        public const string NewAccountRestriction = "NEW_ACCOUNT_RESTRICTION";
        public const string BankChangeCooldown = "BANK_CHANGE_COOLDOWN";
        public const string ClassSessionRequired = "LESSON_REQUIRED";
        public const string PatternDetection = "PATTERN_DETECTION";
    }

    public static class CacheKeys
    {
        public const string VelocityPrefix = "fraud:velocity";
        public const string AmountPrefix = "fraud:amount";

        public static string GetVelocityKey(string tutorId, string period) =>
            $"{VelocityPrefix}:{tutorId}:{period}";

        public static string GetAmountKey(string tutorId, string period) =>
            $"{AmountPrefix}:{tutorId}:{period}";
    }

    public static class ErrorCodes
    {
        public const string FraudCheckFailed = "FRAUD_CHECK_FAILED";
        public const string VelocityExceeded = "VELOCITY_EXCEEDED";
        public const string AmountLimitExceeded = "AMOUNT_LIMIT_EXCEEDED";
        public const string NewAccountRestricted = "NEW_ACCOUNT_RESTRICTED";
        public const string BankChangeCooldownActive = "BANK_CHANGE_COOLDOWN_ACTIVE";
        public const string NoCompletedClassSession = "NO_COMPLETED_LESSON";
        public const string SuspiciousPattern = "SUSPICIOUS_PATTERN";
    }

    public static class ErrorMessages
    {
        public const string VelocityExceededDaily = "You have exceeded the maximum number of withdrawals per day (max 2 per day).";
        public const string VelocityExceededWeekly = "You have exceeded the maximum number of withdrawals per week (max 5 per week).";
        public const string VelocityExceededMonthly = "You have exceeded the maximum number of withdrawals per month (max 15 per month).";

        public const string AmountLimitPerTransaction = "Withdrawal amount exceeds the maximum limit (max 10,000,000 VND per transaction).";
        public const string AmountLimitDaily = "Total withdrawal amount today exceeds the daily limit (max 30,000,000 VND per day).";
        public const string AmountLimitMonthly = "Total withdrawal amount this month exceeds the monthly limit (max 100,000,000 VND per month).";

        public const string NewAccountBlocked = "New accounts cannot withdraw funds within the first 7 days. Please try again later.";
        public const string NewAccountRestricted = "Accounts under 30 days old can only withdraw up to 2,000,000 VND per transaction.";

        public const string BankChangeCooldown = "You must wait 24 hours after changing bank information before withdrawing funds.";
        public const string BankChangeFrequent = "You have changed bank information too many times this month. Your account will be reviewed.";

        public const string NoCompletedClassSession = "You must complete at least 1 class session before withdrawing funds.";

        public const string PatternFullBalance = "Withdrawing nearly all balance (>95%) may require additional review.";
        public const string PatternMultipleIps = "Multiple IP addresses detected in a short period.";
        public const string PatternUnusualAmount = "Withdrawal amount is unusual compared to your transaction history.";
    }
}
