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
    /// <summary>Có yêu cầu rút tiền mới đang chờ duyệt, gửi cho Admin (hiển thị ở CMS).
    /// Referenceid = withdrawalId. Tách khỏi <see cref="WithdrawalRequest"/> vì loại đó dành cho
    /// người yêu cầu (đã tạo / được duyệt / bị từ chối), còn loại này là việc cần Admin xử lý.</summary>
    public const string WithdrawalRequestNew = "withdrawal_request_new";
    public const string LessonReminder  = "lesson_reminder";
    public const string LessonCheckin   = "lesson_checkin";
    public const string LessonReport    = "lesson_report";
    public const string LessonConfirmed = "lesson_confirmed";
    public const string LessonConfirmDeadline = "lesson_confirm_deadline";
    public const string LessonNoShow    = "lesson_no_show";

    /// <summary>Buổi học trôi qua mà hệ thống không ghi nhận ai vào lớp.</summary>
    public const string LessonNoAttendance = "lesson_no_attendance";
    public const string LessonScheduleChange = "lesson_schedule_change";
    /// <summary>Có đề xuất dời giờ học mới, gửi cho người đối ứng cần phản hồi. Referenceid = classSessionId.</summary>
    public const string RescheduleProposed = "reschedule_proposed";
    /// <summary>Đề xuất dời giờ học đã được đối ứng đồng ý, giờ học đã cập nhật. Referenceid = classSessionId.</summary>
    public const string RescheduleAccepted = "reschedule_accepted";
    /// <summary>Đề xuất dời giờ học đã bị đối ứng từ chối. Referenceid = classSessionId.</summary>
    public const string RescheduleRejected = "reschedule_rejected";
    /// <summary>Đề xuất dời giờ học đã hết hạn do không ai phản hồi kịp. Referenceid = classSessionId.</summary>
    public const string RescheduleExpired = "reschedule_expired";
    /// <summary>Có khiếu nại/tranh chấp mới cần CMS xử lý — gửi cho Admin/Staff.
    /// Referenceid = disputeId (CMS route là disputes/:disputeId).</summary>
    public const string DisputeNew      = "dispute_new";
    /// <summary>Bên bị khiếu nại được báo có khiếu nại về buổi học của mình.
    /// Referenceid = classSessionId (app người dùng route là disputes/:classSessionId).</summary>
    public const string DisputeReceived = "dispute_received";
    /// <summary>Khiếu nại đã xử lý xong (giải quyết / đóng / xác nhận vắng mặt). Referenceid = classSessionId.</summary>
    public const string DisputeResolved = "dispute_resolved";
    /// <summary>Bên đối ứng vừa gửi phản hồi cho khiếu nại. Referenceid = classSessionId.</summary>
    public const string DisputeResponded = "dispute_responded";
    /// <summary>Gia sư nộp thay đổi hồ sơ, chờ CMS duyệt. Không có referenceid (CMS mở thẳng tab chờ duyệt).</summary>
    public const string TutorProfileUpdateRequest = "tutor_profile_update";
    /// <summary>Hồ sơ hoặc chứng chỉ gia sư đã được duyệt.</summary>
    public const string TutorVettingApproved = "tutor_vetting_approved";
    /// <summary>Hồ sơ hoặc chứng chỉ gia sư bị từ chối.</summary>
    public const string TutorVettingRejected = "tutor_vetting_rejected";
    /// <summary>Tiền khóa học đã giải ngân về ví gia sư. Referenceid = bookingId.</summary>
    public const string SettlementReleased = "settlement_released";
    /// <summary>Khóa học đã kết thúc/chốt sổ. Referenceid = bookingId.</summary>
    public const string CourseCompleted = "course_completed";
    public const string Message         = "message";
    /// <summary>Tin nhắn mới trong hội thoại hỗ trợ giữa người dùng và Admin. Referenceid = supportThreadId.</summary>
    public const string SupportMessage  = "support_message";
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
    /// <summary>Tripwire: tài khoản ngân hàng của chính người dùng vừa được thêm/sửa.</summary>
    public const string BankAccountUpdated = "bank_account_updated";
    /// <summary>Tripwire: tài khoản ngân hàng của chính người dùng vừa bị xoá.</summary>
    public const string BankAccountDeleted = "bank_account_deleted";
    /// <summary>Video buổi học đã relay xong lên Drive, xem lại được (và học sinh/gia sư có thể dùng AI).
    /// Referenceid = classSessionId.</summary>
    public const string LessonRecordingReady = "lesson_recording_ready";
    /// <summary>Gemini đã tóm tắt xong video buổi học, học sinh có thể xem lại. Referenceid = classSessionId.</summary>
    public const string LessonVideoSummaryReady = "lesson_video_summary_ready";
    /// <summary>Gemini đã điền xong nội dung báo cáo gợi ý cho gia sư. Referenceid = classSessionId.</summary>
    public const string LessonReportAiFillReady = "lesson_report_ai_fill_ready";
    /// <summary>Buổi học bị ngắt giữa chừng, đã tạo buổi phụ để học tiếp trong ngày.
    /// Referenceid = classSessionId của BUỔI PHỤ (không phải buổi gốc).</summary>
    public const string LessonContinuationCreated = "lesson_continuation_created";
    /// <summary>Buổi phụ quá hạn trong ngày mà không ai quay lại học, hệ thống đã tự đóng buổi gốc
    /// thành hoàn tất. Referenceid = classSessionId của buổi GỐC.</summary>
    public const string LessonInterruptionAutoClosed = "lesson_interruption_auto_closed";
    /// <summary>Buổi phụ đã được tạo nhưng hệ thống không tự tìm được khe giờ trống nào cho gia sư
    /// sau khi tự dời giờ nhiều lần — gia sư cần chủ động đề xuất đổi lịch. Referenceid = classSessionId
    /// của BUỔI PHỤ (không phải buổi gốc).</summary>
    public const string LessonContinuationScheduleConflict = "lesson_continuation_schedule_conflict";
    /// <summary>Admin/Staff đã đóng một tranh chấp theo hướng học lại và lên lịch buổi học lại mới.
    /// Referenceid = classSessionId của buổi HỌC LẠI mới tạo.</summary>
    public const string DisputeRelearnScheduled = "dispute_relearn_scheduled";
}
