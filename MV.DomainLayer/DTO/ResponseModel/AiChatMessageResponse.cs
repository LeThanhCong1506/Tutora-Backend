namespace MV.DomainLayer.DTO.ResponseModel;

public class AiChatMessageResponse
{
    public string MessageId { get; set; } = null!;
    public string SessionId { get; set; } = null!;
    public string Role { get; set; } = null!;
    public string Content { get; set; } = null!;
    public string? ImageUrl { get; set; }
    public object? Metadata { get; set; }
    public bool NoteSaved { get; set; }
    public DateTime CreatedAt { get; set; }
}
