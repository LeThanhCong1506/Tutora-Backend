namespace MV.DomainLayer.DTO.ResponseModel;

public class ChatMessageResponse
{
    public int MessageId { get; set; }
    public int ChannelId { get; set; }
    public string SenderId { get; set; } = null!;
    public string? SenderName { get; set; }
    public string? SenderAvatarUrl { get; set; }
    public string Content { get; set; } = null!;
    public string MessageType { get; set; } = null!;
    public DateTime? CreatedAt { get; set; }
    public object? Metadata { get; set; }
    public bool IsRead { get; set; }
    public DateTime? ReadAt { get; set; }
}
