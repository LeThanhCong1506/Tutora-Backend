using MV.DomainLayer.Constants;
using MV.DomainLayer.Helpers;
using static MV.DomainLayer.Constants.ClassSessionStatus;

namespace MV.DomainLayer.DTO.ResponseModel;

/// <summary>
/// Rich classSession detail — returned by all tutor action endpoints and parent classSession queries.
/// Callers: <c>GetTutorClassSessionDetailAsync</c>, <c>CheckInAsync</c>, <c>CheckOutAsync</c>,
/// <c>SubmitReportAsync</c>, <c>ReportTutorNoShowAsync</c>, <c>CreateMakeupClassSessionAsync</c>,
/// <c>GetClassSessionDetailAsync</c> (parent/tutor via ParentClassSessionService).
/// Includes attendance, report content, makeup info, and computed time-check helpers.
/// All DateTime fields are Vietnam time (UTC+7).
/// </summary>
public class ClassSessionDetailResponse
{
    // Tất cả comparison dùng giờ VN vì ScheduledStart/ConfirmDeadline đã là giờ VN (now using VietnamTimeHelper)

    public int ClassSessionId { get; set; }
    public int? BookingId { get; set; }

    // Schedule info
    public DateTime ScheduledStart { get; set; }
    public DateTime ScheduledEnd { get; set; }
    public DateTime? RealStart { get; set; }
    public DateTime? RealEnd { get; set; }

    // Attendance
    public DateTime? CheckInTime { get; set; }
    public DateTime? CheckOutTime { get; set; }
    public bool? IsTutorPresent { get; set; }
    public bool? IsStudentPresent { get; set; }
    public string? AttendanceNote { get; set; }

    // Status
    public string? Status { get; set; }
    public string? BookingStatus { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public DateTime? ConfirmDeadline { get; set; }
    public DateTime? ParentAckAt { get; set; }
    public bool? IsSettled { get; set; }

    // Content
    public string? ClassSessionContent { get; set; }
    public string? Homework { get; set; }
    public string? TutorNotes { get; set; }
    public string? MeetingLink { get; set; }

    /// <summary>
    /// True nếu đây là buổi TIẾP THEO nhưng phụ huynh chưa thanh toán đợt 2 (các buổi
    /// còn lại). FE dùng cờ này để khóa link/nút vào lớp và hiển thị CTA thanh toán.
    /// </summary>
    public bool RequiresRemainingPayment { get; set; }

    // Price info
    public decimal? ClassSessionPrice { get; set; }

    // Makeup info
    public bool? IsMakeup { get; set; }
    public int? OriginalClassSessionId { get; set; }
    public string? NoShowAction { get; set; }

    // Interruption / continuation info (Link 2) — Originalsessionid dùng chung ở trên.
    /// <summary>True nếu đây là buổi phụ, sinh ra khi buổi gốc (<see cref="OriginalClassSessionId"/>) bị báo ngắt giữa chừng.</summary>
    public bool? IsContinuation { get; set; }
    /// <summary>True nếu đây là buổi học lại (Link 3), sinh ra khi hoà giải dispute chọn "học lại".</summary>
    public bool? IsDisputeRelearn { get; set; }
    /// <summary>Mốc buổi GỐC bị báo ngắt (chỉ có giá trị trên chính buổi gốc, không phải trên buổi phụ).</summary>
    public DateTime? InterruptedAt { get; set; }
    /// <summary>Lý do báo ngắt do người báo tự nhập (chỉ có trên buổi gốc).</summary>
    public string? InterruptReason { get; set; }
    /// <summary>Tên người đã báo ngắt (chỉ có trên buổi gốc) — KHÔNG trả user_id ra ngoài, chỉ tên đã resolve.</summary>
    public string? InterruptedByName { get; set; }
    /// <summary>ID buổi phụ sinh ra từ chính buổi này khi bị ngắt (chỉ có trên buổi GỐC, status=interrupted).
    /// Null nếu buổi chưa từng bị ngắt. FE dùng để gọi API xác nhận/xem trạng thái đồng ý bỏ buổi phụ.</summary>
    public int? ContinuationSessionId { get; set; }
    /// <summary>Giờ hẹn của buổi phụ (<see cref="ContinuationSessionId"/>) — FE hiện trực tiếp trên
    /// trang buổi GỐC thay vì bắt phải mở thêm trang riêng của buổi phụ mới biết giờ học nốt.</summary>
    public DateTime? ContinuationScheduledStart { get; set; }
    public DateTime? ContinuationScheduledEnd { get; set; }
    /// <summary>True khi CẢ HAI phía đã đồng ý bỏ hẳn buổi phụ (<see cref="ContinuationSessionId"/>) —
    /// lúc này gia sư nộp được báo cáo cho buổi GỐC dù đang ở status=interrupted (không cần
    /// in_progress). Luôn false nếu chưa từng bị ngắt hoặc chưa đủ 2 phía xác nhận.</summary>
    public bool ContinuationSkipBothConfirmed { get; set; }
    /// <summary>True khi CHÍNH buổi này là buổi phụ (<see cref="IsContinuation"/>) và cả 2 phía đã
    /// đồng ý bỏ nó (khác <see cref="ContinuationSkipBothConfirmed"/> — field đó nói về buổi phụ CỦA
    /// buổi này khi xem từ buổi GỐC). FE dùng để khoá nút "Vào học nhanh"/"Đề xuất đổi lịch" trên
    /// chính trang buổi phụ một khi 2 bên đã thống nhất bỏ, dù status vẫn còn Scheduled cho tới khi
    /// gia sư nộp báo cáo xong (xem SubmitReportAsync).</summary>
    public bool SkipConfirmedByBothSides { get; set; }

    // Related entities
    public ClassSessionStudentResponse? Student { get; set; }
    public ClassSessionTutorResponse? Tutor { get; set; }
    public ClassSessionSubjectResponse? Subject { get; set; }
    public ClassSessionReportResponse? Report { get; set; }

    /// <summary>Lịch sử dời lịch (nếu có) — bao gồm cả yêu cầu đã áp dụng, đang chờ, hoặc bị từ chối.</summary>
    public List<DisputeScheduleChangeAuditResponse> ScheduleChanges { get; set; } = new();

    /// <summary>Đề xuất đổi lịch đang chờ phản hồi (nếu có), null nếu không có đề xuất nào đang chờ.</summary>
    public ClassSessionRescheduleProposalResponse? PendingRescheduleProposal { get; set; }

    /// <summary>Toàn bộ lịch sử đề xuất đổi lịch (đã đồng ý/từ chối/hết hạn), mới nhất trước.</summary>
    public List<ClassSessionRescheduleProposalResponse> RescheduleProposals { get; set; } = new();

    // Time calculations (so sánh với giờ VN vì ScheduledStart/ConfirmDeadline đã là giờ VN)
    public TimeSpan? TimeUntilStart => ScheduledStart > TimeZoneHelper.UtcNow
        ? ScheduledStart - TimeZoneHelper.UtcNow
        : null;

    public TimeSpan? TimeRemainingToConfirm => ConfirmDeadline.HasValue && ConfirmDeadline > TimeZoneHelper.UtcNow
        ? ConfirmDeadline - TimeZoneHelper.UtcNow
        : null;

    public bool CanCheckIn => Status == Scheduled &&
        Math.Abs((TimeZoneHelper.UtcNow - ScheduledStart).TotalMinutes) <= 15;

    // Buổi bị ngắt (status=interrupted) nộp báo cáo được luôn, không còn cần chờ 2 phía đồng ý bỏ
    // buổi phụ nữa (điều kiện ContinuationSkipBothConfirmed đã bỏ hẳn) — khớp guard thật trong
    // SubmitReportAsync: cho phép khi Status là InProgress HOẶC Interrupted.
    public bool CanSubmitReport => (Status == InProgress || Status == Interrupted) && CheckInTime.HasValue;
}

public class ClassSessionStudentResponse
{
    public string? StudentId { get; set; }
    public string? FullName { get; set; }
    public string? School { get; set; }
    public string? GradeLevel { get; set; }
    public string? AvatarUrl { get; set; }
}

public class ClassSessionTutorResponse
{
    public string? TutorId { get; set; }
    public string? FullName { get; set; }
    public string? AvatarUrl { get; set; }
    public double? AverageRating { get; set; }
}

public class ClassSessionSubjectResponse
{
    public int SubjectId { get; set; }
    public string? SubjectName { get; set; }
}

public class ClassSessionReportResponse
{
    public int ReportId { get; set; }
    public string? ContentCovered { get; set; }
    public string? HomeworkAssigned { get; set; }
    public int? StudentPerformanceRating { get; set; }
    /// <summary>URL các tệp đính kèm — giữ lại cho client cũ.</summary>
    public List<string>? Attachments { get; set; }

    /// <summary>Tệp đính kèm kèm mô tả gia sư đặt; client mới nên đọc trường này.</summary>
    public List<ReportAttachment>? AttachmentDetails { get; set; }

    public DateTime? CreatedAt { get; set; }
}
