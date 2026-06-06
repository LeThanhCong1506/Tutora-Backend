namespace MV.DomainLayer.DTO.RequestModel;

/// <summary>
/// Request body for applying a suspension to a user.
/// </summary>
public class SuspensionRequest
{
    public string SuspensionType { get; set; } = MV.DomainLayer.Constants.SuspensionType.Temporary;
    public string Reason { get; set; } = null!;
    public int? DurationDays { get; set; }
}
