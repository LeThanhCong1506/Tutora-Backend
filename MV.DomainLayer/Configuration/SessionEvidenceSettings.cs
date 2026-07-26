namespace MV.DomainLayer.Configuration;

/// <summary>
/// Tuning for the attendance evidence captured during a lesson (heartbeat chain and the network /
/// device record behind it).
/// </summary>
public class SessionEvidenceSettings
{
    public const string SectionName = "SessionEvidence";

    /// <summary>
    /// Read the client address from <c>X-Forwarded-For</c> instead of the socket.
    ///
    /// Off by default on purpose: the header is attacker-controlled unless a trusted reverse proxy
    /// is guaranteed to overwrite it, and a forged address in the fraud record would be worse than
    /// no address at all. Turn this on only when the app sits behind such a proxy.
    /// </summary>
    public bool TrustForwardedFor { get; set; } = false;

    /// <summary>
    /// How long a heartbeat interval may go without a beat before the next beat is treated as a new
    /// interval rather than a continuation. Must stay above the client beat period (~20s) plus room
    /// for one lost request; the in-memory presence window uses 60s for the same reason.
    /// </summary>
    public int HeartbeatGapSeconds { get; set; } = 75;

    /// <summary>
    /// How recent the last 10-second lobby refresh must be before the admin log may say the person
    /// is currently waiting. An unclosed row older than this is historical evidence, not presence.
    /// </summary>
    public int LobbyHeartbeatGapSeconds { get; set; } = 35;

    /// <summary>
    /// Share of reported beats with no microphone, no camera and an idle tab that makes a
    /// participant's presence look like an empty room. Advisory only — it raises a flag for staff,
    /// it never decides a dispute.
    /// </summary>
    public double IdlePresenceThreshold { get; set; } = 0.8;

    /// <summary>
    /// Minimum reported beats before <see cref="IdlePresenceThreshold"/> is allowed to raise the
    /// flag. Three beats is about a minute of evidence; below that a mid-lesson reload would look
    /// like an absent tutor.
    /// </summary>
    public int MinimumBeatsForIdleVerdict { get; set; } = 3;

    /// <summary>
    /// Grace period before a join counts as late, and after which a leave counts as early. Covers
    /// the ordinary minute of device fiddling at both ends of a lesson.
    /// </summary>
    public int PunctualityGraceSeconds { get; set; } = 300;
}
