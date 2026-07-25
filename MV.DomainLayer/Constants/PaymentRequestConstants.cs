namespace MV.DomainLayer.Constants;

public static class PaymentRequestProvider
{
    public const string PayOS = "PayOS";
}

public static class PaymentRequestStatus
{
    public const string Pending = "PENDING";
    public const string Processing = "PROCESSING";
    public const string Paid = "PAID";
    public const string Cancelled = "CANCELLED";
    public const string Expired = "EXPIRED";
    public const string Superseded = "SUPERSEDED";
    public const string RequiresReview = "REQUIRES_REVIEW";
    public const string Unknown = "UNKNOWN";

    public static bool IsActive(string? status)
        => status is Pending or Processing or RequiresReview or Unknown;
}

public static class PaymentRequestPhase
{
    public const string Deposit = PaymentPhase.Deposit;
    public const string Remaining = PaymentPhase.Remaining;
    public const string LegacyUnknown = "legacy_unknown";

    /// <summary>Thanh toán mua gói AI credit (không thuộc booking nào).</summary>
    public const string AiCredit = "ai_credit";
}

public static class PaymentCaptureSource
{
    public const string Webhook = "Webhook";
    public const string Polling = "Polling";
    public const string Manual = "Manual";
    public const string InternalWallet = "InternalWallet";
    public const string Legacy = "Legacy";
}

public static class PaymentReconciliationStatus
{
    public const string Matched = "Matched";
    public const string NotApplicable = "NotApplicable";
    public const string Partial = "Partial";
    public const string Unexpected = "Unexpected";
    public const string AmountMismatch = "AmountMismatch";
    public const string Orphan = "Orphan";
}

public static class PaymentAlertType
{
    public const string UnexpectedTransaction = "PayOSUnexpectedTransaction";
    public const string AmountMismatch = "PayOSAmountMismatch";
    public const string OrphanTransaction = "PayOSOrphanTransaction";
    public const string ReferenceConflict = "PayOSReferenceConflict";
    public const string PaidWithoutTransaction = "PayOSPaidWithoutTransaction";
    public const string CancellationFailed = "PayOSCancellationFailed";
}
