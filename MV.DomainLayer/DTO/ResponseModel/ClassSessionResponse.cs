namespace MV.DomainLayer.DTO.ResponseModel;

/// <summary>
/// Standard classSession response — used for both list views and basic single-classSession queries.
/// Callers: <c>GetTutorClassSessionsAsync</c>, <c>GetParentClassSessionsAsync</c>, <c>GetClassSessionByIdAsync</c>.
/// For richer tutor/parent actions (check-in, report, no-show), use <see cref="ClassSessionDetailResponse"/>.
/// For student perspective, use <see cref="StudentClassSessionSummaryResponse"/>.
/// </summary>
public class ClassSessionResponse
{
    public int ClassSessionId { get; set; }
    public int BookingId { get; set; }
    public string? TutorId { get; set; }
    public string? StudentId { get; set; }
    public DateTime ScheduledStart { get; set; }
    public DateTime ScheduledEnd { get; set; }
    public string? MeetingLink { get; set; }
    public decimal? ClassSessionPrice { get; set; }
    public string Status { get; set; }
    public DateTime? CheckInTime { get; set; }
    public DateTime? CheckOutTime { get; set; }
    public bool? IsTutorPresent { get; set; }
    public bool? IsStudentPresent { get; set; }
    public DateTime? CreatedAt { get; set; }
    /// <summary>True nếu đã có video buổi học xem lại được (không phân biệt đang xử lý/đang ghi).</summary>
    public bool HasRecording { get; set; }
    public StudentMiniResponse? Student { get; set; }
    public SubjectResponse? Subject { get; set; }
    public TutorMiniResponse? Tutor { get; set; }
}
