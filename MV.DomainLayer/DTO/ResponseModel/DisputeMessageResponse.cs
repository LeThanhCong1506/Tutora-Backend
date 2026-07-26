namespace MV.DomainLayer.DTO.ResponseModel;

/// <summary>A message in one of a dispute's private admin-mediated threads.</summary>
public class DisputeMessageResponse
{
    public int DisputeMessageId { get; set; }
    public int DisputeId { get; set; }
    public string ThreadType { get; set; } = null!;
    public string? SenderId { get; set; }
    public string? SenderName { get; set; }
    public string? SenderRole { get; set; }
    public string Message { get; set; } = null!;
    public DateTime? CreatedAt { get; set; }
}
