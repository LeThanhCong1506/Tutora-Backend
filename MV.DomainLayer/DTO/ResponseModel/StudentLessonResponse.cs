namespace MV.DomainLayer.DTO.ResponseModel;

/// <summary>
/// Student's view of their lesson list.
/// Caller: <c>GetStudentLessonsAsync</c>, <c>GetStudentPendingLessonsAsync</c>.
/// Uses flat string fields (SubjectName, TutorName) instead of nested objects —
/// deliberately lighter than <see cref="LessonResponse"/> for the student portal.
/// Extend with <see cref="StudentLessonDetailResponse"/> for full per-lesson view.
/// </summary>
public class StudentLessonSummaryResponse
{
    public int LessonId { get; set; }
    public string? Status { get; set; }
    public DateTime? ScheduledStart { get; set; }
    public DateTime? ScheduledEnd { get; set; }
    public DateTime? ConfirmDeadline { get; set; }
    public decimal? LessonPrice { get; set; }
    public string? SubjectName { get; set; }
    public string? TutorName { get; set; }
    public int? BookingId { get; set; }
}

/// <summary>
/// Extends <see cref="StudentLessonSummaryResponse"/> with meeting link, attendance times,
/// and the tutor's lesson report. Caller: <c>GetStudentLessonDetailAsync</c>.
/// </summary>
public class StudentLessonDetailResponse : StudentLessonSummaryResponse
{
    public string? MeetingLink { get; set; }
    public DateTime? CheckinTime { get; set; }
    public DateTime? CheckoutTime { get; set; }
    public string? TutorAvatar { get; set; }
    public StudentLessonReportResponse? Report { get; set; }
}

public class StudentLessonReportResponse
{
    public string? TopicsCovered { get; set; }
    public string? HomeworkAssigned { get; set; }
    public string? TutorNotes { get; set; }
}
