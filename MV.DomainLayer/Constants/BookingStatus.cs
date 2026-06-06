namespace MV.DomainLayer.Constants;

public static class BookingStatus
{
    public const string PendingTutor = "pending_tutor";
    public const string Accepted = "accepted";
    public const string PendingPayment = "pending_payment";
    public const string Paid = "paid";
    public const string PaymentTimeout = "payment_timeout";
    public const string Ongoing = "ongoing";
    public const string Completed = "completed";
    public const string Cancelled = "cancelled";
    public const string CancelledNoshow = "cancelled_noshow";
    public const string DepositPaid = "deposit_paid";
    public const string PendingRemainingPayment = "pending_remaining_payment";
}

public static class ChatChannelStatus
{
    public const string Active = "active";
    public const string Closed = "closed";
}

public static class RefundStatus
{
    public const string Refunded = "refunded";
    public const string RefundFailed = "refund_failed";
    public const string NoRefund = "no_refund";
}
