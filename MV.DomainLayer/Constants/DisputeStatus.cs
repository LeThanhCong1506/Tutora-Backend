namespace MV.DomainLayer.Constants;

/// <summary>
/// Dispute status constants
/// </summary>
public static class DisputeStatus
{
    public const string Pending = "pending";
    public const string Investigating = "investigating";
    public const string Resolved = "resolved";
    public const string Closed = "closed";

    public static readonly string[] All = { Pending, Investigating, Resolved, Closed };
}
