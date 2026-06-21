namespace MV.DomainLayer.DTO.ResponseModel;

public class AiChatSessionResponse
{
    public string SessionId { get; set; } = null!;
    public string SessionType { get; set; } = null!;
    public string? Title { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
