namespace MV.DomainLayer.DTO.ResponseModel;

/// <summary>
/// Buổi học của một người con, dạng list phẳng cho app mobile của phụ huynh.
/// Khác <see cref="CalendarDayResponse"/>
/// </summary>
public class ParentChildClassSessionResponse
{
    public int ClassSessionId { get; set; }
    public int? BookingId { get; set; }

    public DateTime ScheduledStart { get; set; }
    public DateTime ScheduledEnd { get; set; }

    public string? StudentId { get; set; }
    public string? StudentName { get; set; }
    public string? TutorId { get; set; }
    public string? TutorName { get; set; }
    public string? TutorAvatarUrl { get; set; }
    public string? SubjectName { get; set; }

    public string? Status { get; set; }
    public string? BookingStatus { get; set; }
    public string? MeetingLink { get; set; }
    public DateTime? CheckOutTime { get; set; }

    /// <summary>True nếu buổi học đã có video xem lại.</summary>
    public bool HasRecording { get; set; }

    /// <summary>True nếu buổi này đang có đề xuất đổi lịch chờ phản hồi.</summary>
    public bool HasPendingReschedule { get; set; }
}
