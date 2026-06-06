using MV.DomainLayer.Constants;

namespace MV.DomainLayer.DTO.ResponseModel;

/// <summary>
/// Response for calendar view - lessons grouped by date
/// </summary>
public class CalendarDayResponse
{
    /// <summary>
    /// Date of the calendar day
    /// </summary>
    public DateTime Date { get; set; }

    /// <summary>
    /// List of lessons on this day
    /// </summary>
    public List<CalendarLessonResponse> Lessons { get; set; } = new();
}

/// <summary>
/// Lesson summary for calendar view
/// </summary>
public class CalendarLessonResponse
{
    public int LessonId { get; set; }
    public DateTime ScheduledStart { get; set; }
    public DateTime ScheduledEnd { get; set; }
    public string? StudentName { get; set; }
    public string? TutorName { get; set; }
    public string? SubjectName { get; set; }
    public string? Status { get; set; }
    public string? MeetingLink { get; set; }

    /// <summary>
    /// Color code based on status for UI rendering
    /// </summary>
    public string StatusColor => Status switch
    {
        LessonStatus.Scheduled => "#3B82F6",           // Blue
        LessonStatus.InProgress => "#22C55E",         // Green
        LessonStatus.PendingConfirmation => "#F59E0B", // Amber
        LessonStatus.Completed => "#10B981",           // Emerald
        LessonStatus.Cancelled => "#6B7280",           // Gray
        LessonStatus.Disputed => "#EF4444",            // Red
        LessonStatus.NoShow => "#DC2626",             // Dark Red
        _ => "#9CA3AF"                      // Default Gray
    };
}
