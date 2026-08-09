namespace MV.DomainLayer.Constants;

/// <summary>
/// Notification type constants used in the `type` column of the notifications table.
/// </summary>
public static class NotificationType
{
    public const string BookingNew      = "booking_new";
    public const string BookingAccepted = "booking_accepted";
    public const string BookingDeclined = "booking_declined";
    public const string BookingCancelled = "booking_cancelled";
    public const string BookingTimeout  = "booking_timeout";
    public const string BookingPaymentDueSoon = "booking_payment_due_soon";
    public const string PaymentSuccess  = "payment_success";
    public const string PaymentRemainingRequired = "payment_remaining_required";
    public const string PaymentRefundSuccess = "payment_refund_success";
    public const string WithdrawalRequest = "withdrawal_request";
    public const string LessonReminder  = "lesson_reminder";
    public const string LessonCheckin   = "lesson_checkin";
    public const string LessonReport    = "lesson_report";
    public const string LessonConfirmed = "lesson_confirmed";
    public const string LessonConfirmDeadline = "lesson_confirm_deadline";
    public const string LessonNoShow    = "lesson_no_show";
    public const string LessonScheduleChange = "lesson_schedule_change";
    public const string Message         = "message";
    public const string Warning         = "warning";
    public const string DisputeMessage  = "dispute_message";
    /// <summary>Khóa học đã hoàn thành, mời người học đánh giá gia sư. Referenceid = bookingId.</summary>
    public const string FeedbackRequest = "feedback_request";
    /// <summary>Gia sư đã phản hồi đánh giá. Referenceid = bookingId.</summary>
    public const string FeedbackReply   = "feedback_reply";
    /// <summary>Gia sư vừa nhận được đánh giá mới từ người học. Referenceid = bookingId.</summary>
    public const string FeedbackReceived = "feedback_received";
    /// <summary>Admin đã ẩn hoặc hiện lại một đánh giá. Referenceid = bookingId.</summary>
    public const string FeedbackModerated = "feedback_moderated";
    /// <summary>Admin/staff vừa chuyển tiền chủ động vào ví. Referenceid = transferId.</summary>
    public const string WalletTransferReceived = "wallet_transfer_received";
}
