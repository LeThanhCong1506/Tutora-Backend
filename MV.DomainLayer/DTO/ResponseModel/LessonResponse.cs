namespace MV.DomainLayer.DTO.ResponseModel;

/// <summary>
/// Standard lesson response — used for both list views and basic single-lesson queries.
/// Callers: <c>GetTutorLessonsAsync</c>, <c>GetParentLessonsAsync</c>, <c>GetLessonByIdAsync</c>.
/// For richer tutor/parent actions (check-in, report, no-show), use <see cref="LessonDetailResponse"/>.
/// For student perspective, use <see cref="StudentLessonSummaryResponse"/>.
/// </summary>
public class LessonResponse
{
    public int LessonId { get; set; }
    public int BookingId { get; set; }
    public string? TutorId { get; set; }
    public string? StudentId { get; set; }
    public DateTime ScheduledStart { get; set; }
    public DateTime ScheduledEnd { get; set; }
    public string? MeetingLink { get; set; }
    public decimal? LessonPrice { get; set; }
    public string Status { get; set; }
    public DateTime? CheckInTime { get; set; }
    public DateTime? CheckOutTime { get; set; }
    public bool? IsTutorPresent { get; set; }
    public bool? IsStudentPresent { get; set; }
    public DateTime? CreatedAt { get; set; }
    public StudentMiniResponse? Student { get; set; }
    public SubjectResponse? Subject { get; set; }
    public TutorMiniResponse? Tutor { get; set; }
}
