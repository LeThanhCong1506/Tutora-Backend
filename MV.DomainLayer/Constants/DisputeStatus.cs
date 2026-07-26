namespace MV.DomainLayer.Constants;

/// <summary>
/// Dispute status constants
/// </summary>
public static class DisputeStatus
{
    public const string Pending = "pending";
    public const string Investigating = "investigating";
    /// <summary>
    /// Admin has verified a tutor no-show. The payer side may now choose the remedy.
    /// </summary>
    public const string ConfirmedNoShow = "confirmed_no_show";
    public const string Resolved = "resolved";
    public const string Closed = "closed";

    public static readonly string[] All = { Pending, Investigating, ConfirmedNoShow, Resolved, Closed };
}
