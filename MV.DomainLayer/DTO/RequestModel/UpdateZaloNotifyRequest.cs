namespace MV.DomainLayer.DTO.RequestModel;

/// <summary>
/// Request body for updating a user's Zalo notification preference.
/// </summary>
public record UpdateZaloNotifyRequest(bool ZaborNotifyEnabled);
