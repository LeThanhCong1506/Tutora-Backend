namespace MV.DomainLayer.DTO.RequestModel;

/// <summary>
/// Optional context when reporting a tutor no-show. Reason/evidence are advisory context
/// for admin, not a gate — the classSession status check is the only enforced precondition.
/// </summary>
public class ReportNoShowRequest
{
    /// <summary>User-adjustable time of the no-show, for the dispute reason text. Defaults to now.</summary>
    public DateTime? ReportedAt { get; set; }

    public string? Reason { get; set; }
}
