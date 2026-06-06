namespace MV.DomainLayer.Constants;

public static class TrustScoringConstants
{
    public static class Decisions
    {
        public const string AutoApprove = "AUTO_APPROVE";
        public const string AdminApproved = "ADMIN_APPROVED";
        public const string StaffApproved = "STAFF_APPROVED";
        public const string Delayed = "DELAYED";
        public const string ManualReview = "MANUAL_REVIEW";
        public const string AutoReject = "AUTO_REJECT";
    }

    public static class PositiveFactors
    {
        public const string AccountAge90Plus = "ACCOUNT_AGE_90_PLUS";
        public const string AccountAge30To90 = "ACCOUNT_AGE_30_TO_90";
        public const string CompletedLessons10Plus = "COMPLETED_LESSONS_10_PLUS";
        public const string CompletedLessons5To10 = "COMPLETED_LESSONS_5_TO_10";
        public const string BankVerified = "BANK_VERIFIED";
        public const string NoDisputes = "NO_DISPUTES";
        public const string SmallAmount = "SMALL_AMOUNT";
        public const string HasSuccessfulWithdrawal = "HAS_SUCCESSFUL_WITHDRAWAL";
    }

    public static class NegativeFactors
    {
        public const string NewAccount = "NEW_ACCOUNT";
        public const string BankChangedRecently = "BANK_CHANGED_RECENTLY";
        public const string MultipleIps = "MULTIPLE_IPS";
        public const string FirstWithdrawal = "FIRST_WITHDRAWAL";
        public const string FullBalanceWithdrawal = "FULL_BALANCE_WITHDRAWAL";
        public const string LargeAmount = "LARGE_AMOUNT";
    }

    public static class Messages
    {
        public const string AutoApproveMessage = "Withdrawal request approved automatically. Funds will be transferred within minutes.";
        public const string DelayedMessage = "Withdrawal request is being processed. Funds will be transferred within 2 hours.";
        public const string ManualReviewMessage = "Withdrawal request requires manual review. We will process it within 24 hours.";
        public const string AutoRejectMessage = "Withdrawal request rejected due to security criteria not met.";
    }

    public static class ErrorCodes
    {
        public const string LowTrustScore = "LOW_TRUST_SCORE";
        public const string FraudDetected = "FRAUD_DETECTED";
    }
}
