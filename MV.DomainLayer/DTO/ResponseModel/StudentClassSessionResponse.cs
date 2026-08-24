namespace MV.DomainLayer.DTO.ResponseModel;

/// <summary>
/// Student's view of their classSession list.
/// Caller: <c>GetStudentClassSessionsAsync</c>, <c>GetStudentPendingClassSessionsAsync</c>.
/// Uses flat string fields (SubjectName, TutorName) instead of nested objects —
/// deliberately lighter than <see cref="ClassSessionResponse"/> for the student portal.
/// Extend with <see cref="StudentClassSessionDetailResponse"/> for full per-classSession view.
/// </summary>
public class StudentClassSessionSummaryResponse
{
    public int ClassSessionId { get; set; }
    public string? Status { get; set; }
    public string? BookingStatus { get; set; }
    public bool? IsSettled { get; set; }
    public DateTime? ScheduledStart { get; set; }
    public DateTime? ScheduledEnd { get; set; }
    public DateTime? ConfirmDeadline { get; set; }
    public decimal? ClassSessionPrice { get; set; }
    public string? SubjectName { get; set; }
    public string? TutorName { get; set; }
    public int? BookingId { get; set; }
    /// <summary>True nếu đây là buổi phụ (Link 2), sinh ra khi buổi gốc (<see cref="OriginalClassSessionId"/>) bị báo ngắt giữa chừng.</summary>
    public bool? IsContinuation { get; set; }
    /// <summary>True nếu đây là buổi học lại (Link 3), sinh ra khi hoà giải dispute chọn "học lại".</summary>
    public bool? IsDisputeRelearn { get; set; }
    /// <summary>Buổi gốc mà buổi phụ/buổi học lại này trỏ về — null nếu đây là buổi gốc.</summary>
    public int? OriginalClassSessionId { get; set; }
}

/// <summary>
/// Extends <see cref="StudentClassSessionSummaryResponse"/> with meeting link, attendance times,
/// and the tutor's classSession report. Caller: <c>GetStudentClassSessionDetailAsync</c>.
/// </summary>
public class StudentClassSessionDetailResponse : StudentClassSessionSummaryResponse
{
    public string? MeetingLink { get; set; }
    public DateTime? CheckinTime { get; set; }
    public DateTime? CheckoutTime { get; set; }
    public string? TutorAvatar { get; set; }
    public StudentClassSessionReportResponse? Report { get; set; }

    /// <summary>True khi CHÍNH buổi này là buổi phụ (<see cref="StudentClassSessionSummaryResponse.IsContinuation"/>)
    /// và cả 2 phía đã đồng ý bỏ nó — FE dùng để khoá "Vào học nhanh"/"Đề xuất đổi lịch" dù status
    /// vẫn còn Scheduled cho tới khi gia sư nộp báo cáo xong.</summary>
    public bool SkipConfirmedByBothSides { get; set; }

    /// <summary>
    /// True nếu buổi tiếp theo bị khóa do phụ huynh chưa thanh toán đợt 2.
    /// FE dùng để khóa link vào lớp.
    /// </summary>
    public bool RequiresRemainingPayment { get; set; }

    /// <summary>Lịch sử dời lịch (nếu có) — bao gồm cả yêu cầu đã áp dụng, đang chờ, hoặc bị từ chối.</summary>
    public List<DisputeScheduleChangeAuditResponse> ScheduleChanges { get; set; } = new();

    /// <summary>Đề xuất đổi lịch đang chờ phản hồi (nếu có), null nếu không có đề xuất nào đang chờ.</summary>
    public ClassSessionRescheduleProposalResponse? PendingRescheduleProposal { get; set; }

    /// <summary>Toàn bộ lịch sử đề xuất đổi lịch (đã đồng ý/từ chối/hết hạn), mới nhất trước.</summary>
    public List<ClassSessionRescheduleProposalResponse> RescheduleProposals { get; set; } = new();
}

public class StudentClassSessionReportResponse
{
    public string? TopicsCovered { get; set; }
    public string? HomeworkAssigned { get; set; }
    public string? TutorNotes { get; set; }
    public int? StudentPerformanceRating { get; set; }
    public List<string>? Attachments { get; set; }
}
