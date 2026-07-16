namespace MV.DomainLayer.DTO.ResponseModel;

/// <summary>
/// Minimal classSession payload — only fields needed for meeting link notifications.
/// Used exclusively by <see cref="IChatService.SendMeetLinksAsync"/> after auto-creating classSessions.
/// For a full classSession list, use <see cref="ClassSessionResponse"/>.
/// </summary>
public class ClassSessionMiniResponse
{
    public int ClassSessionId { get; set; }
    public int BookingId { get; set; }
    public int SessionIndex { get; set; }
    public DateTime ScheduledStart { get; set; }
    public DateTime ScheduledEnd { get; set; }
    public string? MeetingLink { get; set; }
    public string Status { get; set; } = "";
}
