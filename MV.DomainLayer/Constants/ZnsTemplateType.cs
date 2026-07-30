namespace MV.DomainLayer.Constants;

/// <summary>
/// Zalo Notification Service (ZNS) template type keys.
/// These are the switch-case keys used to resolve ZNS template IDs from configuration.
/// </summary>
public static class ZnsTemplateType
{
    /// <summary>Nhắc trước giờ học ~30 phút.</summary>
    public const string LessonReminder = "lesson_reminder";

    /// <summary>
    /// Nhắc thanh toán — gộp: cọc sắp hết hạn (30p), đợt 2 sắp hết hạn (24h/1h),
    /// đến hạn đóng đợt 2 (48h sau khi buổi đầu được xác nhận).
    /// </summary>
    public const string PaymentReminder = "payment_reminder";

    /// <summary>Thanh toán thành công — đóng cọc hoặc đóng đợt 2 qua số dư ví.</summary>
    public const string PaymentSuccess = "payment_success";

    /// <summary>
    /// Booking bị hủy kèm hoàn tiền — gộp: quá hạn đóng cọc, gia sư im lặng 24h,
    /// gia sư từ chối, một bên chủ động hủy.
    /// </summary>
    public const string BookingCancelled = "booking_cancelled";

    /// <summary>
    /// Nhắc xác nhận buổi học — gộp: gia sư vừa nộp báo cáo (còn 24h),
    /// còn 2h chót trước khi hệ thống tự động xác nhận.
    /// </summary>
    public const string LessonConfirmReminder = "lesson_confirm_reminder";

    /// <summary>Kết quả yêu cầu rút tiền của gia sư — đã duyệt &amp; chuyển, hoặc bị từ chối &amp; hoàn ví.</summary>
    public const string PayoutResult = "payout_result";

    /// <summary>Kết quả xử lý khiếu nại/vắng mặt — kèm % hoàn tiền nếu có.</summary>
    public const string DisputeResult = "dispute_result";
}
