using MV.DomainLayer.Constants;

namespace MV.DomainLayer.DTO.ResponseModel;

/// <summary>
/// Response for calendar view - classSessions grouped by date
/// </summary>
public class CalendarDayResponse
{
    /// <summary>
    /// Date of the calendar day
    /// </summary>
    public DateTime Date { get; set; }

    /// <summary>
    /// List of classSessions on this day
    /// </summary>
    public List<CalendarClassSessionResponse> ClassSessions { get; set; } = new();
}

/// <summary>
/// ClassSession summary for calendar view
/// </summary>
public class CalendarClassSessionResponse
{
    public int ClassSessionId { get; set; }

    /// <summary>
    /// Booking chứa buổi học — FE tutor dùng để điều hướng tới trang chi tiết lớp
    /// (/tutor-portal/classes/:bookingId) khi bấm vào một buổi trên lịch.
    /// </summary>
    public int? BookingId { get; set; }

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
        ClassSessionStatus.Scheduled => "#3B82F6",           // Blue
        ClassSessionStatus.InProgress => "#22C55E",         // Green
        ClassSessionStatus.PendingConfirmation => "#F59E0B", // Amber
        ClassSessionStatus.Completed => "#10B981",           // Emerald
        ClassSessionStatus.Cancelled => "#6B7280",           // Gray
        ClassSessionStatus.Disputed => "#EF4444",            // Red
        ClassSessionStatus.NoShow => "#DC2626",             // Dark Red
        _ => "#9CA3AF"                      // Default Gray
    };
}
