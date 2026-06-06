namespace MV.DomainLayer.DTO.RequestModel;

/// <summary>
/// Request body for creating or getting an existing chat channel with a tutor.
/// </summary>
public class CreateChannelRequest
{
    public string TutorId { get; set; } = string.Empty;
}
