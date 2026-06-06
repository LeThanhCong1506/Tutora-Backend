namespace MV.DomainLayer.DTO.ResponseModel;

/// <summary>
/// Minimal lesson payload — only fields needed for meeting link notifications.
/// Used exclusively by <see cref="IChatService.SendMeetLinksAsync"/> after auto-creating lessons.
/// For a full lesson list, use <see cref="LessonResponse"/>.
/// </summary>
public class LessonMiniResponse
{
    public int LessonId { get; set; }
    public int BookingId { get; set; }
    public int SessionIndex { get; set; }
    public DateTime ScheduledStart { get; set; }
    public DateTime ScheduledEnd { get; set; }
    public string? MeetingLink { get; set; }
    public string Status { get; set; } = "";
}
