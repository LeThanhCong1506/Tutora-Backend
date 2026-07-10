namespace MV.DomainLayer.DTO.RequestModel;

/// <summary>
/// Heartbeat presence ping gửi từ client trong lúc đang ở trong kênh Agora.
/// </summary>
public class PresencePingRequest
{
    public bool MicOn { get; set; }
    public bool CamOn { get; set; }
}
