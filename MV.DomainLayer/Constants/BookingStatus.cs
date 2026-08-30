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
    /// <summary>
    /// Staff/admin hủy booking sau khi xác minh phụ huynh "nghỉ ngang" (ghost, không còn phản
    /// hồi) ngoài hệ thống (qua tổng đài) — tách khỏi <see cref="CancelledNoshow"/> vì đây không
    /// phải trường hợp gia sư vắng mặt và không nên lẫn vào số liệu "tutor no-show". Gia sư nhận
    /// tiền các buổi đã dạy; phụ huynh được hoàn tiền các buổi chưa dạy.
    /// </summary>
    public const string CancelledByStaff = "cancelled_by_staff";
    /// <summary>
    /// Khóa học bị hủy (các buổi chưa dạy) theo kết quả giải quyết tranh chấp — PHHS được hoàn
    /// tiền phần chưa học (giá gốc, không phí dịch vụ), gia sư không nhận tiền các buổi đó.
    /// </summary>
    public const string CancelledByDispute = "cancelled_by_dispute";
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
