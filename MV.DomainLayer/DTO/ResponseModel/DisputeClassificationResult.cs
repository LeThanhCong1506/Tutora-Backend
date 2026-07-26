namespace MV.DomainLayer.DTO.ResponseModel;

/// <summary>
/// Result of AI-based dispute priority classification. See <see cref="MV.DomainLayer.Constants.DisputePriority"/>.
/// </summary>
public class DisputeClassificationResult
{
    public string Priority { get; set; } = null!;
    public string? Reason { get; set; }
}
