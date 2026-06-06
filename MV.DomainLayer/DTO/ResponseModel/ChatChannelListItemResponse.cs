namespace MV.DomainLayer.DTO.ResponseModel;

public class ChatChannelListItemResponse
{
    public int ChannelId { get; set; }
    public int? BookingId { get; set; }
    public string OtherUserId { get; set; } = null!;
    public string? OtherUserName { get; set; }
    public string? OtherUserAvatarUrl { get; set; }
    public string? Status { get; set; }
    public DateTime? LastMessageAt { get; set; }
    public string? LastMessagePreview { get; set; }
    public int UnreadCount { get; set; }
}
