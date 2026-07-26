namespace MV.DomainLayer.Constants;

/// <summary>
/// Why a heartbeat interval stopped, as stored in <c>session_presence_intervals.closed_reason</c>.
/// A null column means the interval is still open.
/// </summary>
public static class PresenceIntervalCloseReason
{
    /// <summary>The client called the leave endpoint — an intentional exit.</summary>
    public const string Leave = "leave";

    /// <summary>Beats stopped arriving for longer than the presence window (crash, tab closed, network).</summary>
    public const string Gap = "gap";
}
