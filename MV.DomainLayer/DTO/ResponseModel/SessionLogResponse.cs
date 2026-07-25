namespace MV.DomainLayer.DTO.ResponseModel;

/// <summary>
/// Independent, server-side account of who was in a lesson room and when, rebuilt from the Agora
/// channel events captured in <c>agora_channel_events</c>. Staff use this to settle disputes, so
/// every derived number ships alongside the raw timeline it came from.
/// </summary>
public class SessionLogResponse
{
    public int ClassSessionId { get; set; }

    public SessionLogSummary Summary { get; set; } = new();

    public List<SessionLogParticipant> Participants { get; set; } = [];

    public List<SessionLogEvent> Timeline { get; set; } = [];

    /// <summary>
    /// The second, independent evidence source: the chain of presence heartbeats our own classroom
    /// client sent. One entry per application user, so it stays separate from the Agora-uid-keyed
    /// <see cref="Participants"/> list rather than being silently mixed into it.
    /// </summary>
    public List<SessionLogHeartbeat> Heartbeats { get; set; } = [];

    /// <summary>Networks and devices each participant was admitted from during this lesson.</summary>
    public List<SessionLogDeviceUse> Devices { get; set; } = [];

    /// <summary>Machine-readable warnings; see <see cref="SessionLogFlag"/> for the vocabulary.</summary>
    public List<string> Flags { get; set; } = [];
}

public class SessionLogSummary
{
    /// <summary>One UTC instant used for every provisional calculation in this response.</summary>
    public DateTime SnapshotAt { get; set; }

    /// <summary>True while the lesson can still receive new Agora presence events.</summary>
    public bool IsOngoing { get; set; }

    /// <summary>
    /// True only when the evidence is final and complete enough to support no-show/refund guidance.
    /// </summary>
    public bool IsEvidenceConclusive { get; set; }

    public DateTime ScheduledStart { get; set; }
    public DateTime ScheduledEnd { get; set; }
    public int ScheduledSeconds { get; set; }

    /// <summary>Check-in recorded by our own presence heartbeat, for cross-checking against Agora.</summary>
    public DateTime? CheckInTime { get; set; }
    public DateTime? CheckOutTime { get; set; }

    public DateTime? FirstEventAt { get; set; }
    public DateTime? LastEventAt { get; set; }

    public int TutorSeconds { get; set; }
    public int StudentSeconds { get; set; }

    /// <summary>Seconds the tutor and the student were in the room at the same time — the number
    /// that actually answers "did this lesson happen".</summary>
    public int OverlapSeconds { get; set; }

    /// <summary>OverlapSeconds / ScheduledSeconds, rounded to 4 decimals. 0 when unscheduled.</summary>
    public double OverlapRatio { get; set; }

    /// <summary>Suggested refund percentage derived from <see cref="OverlapRatio"/>. Advisory only —
    /// staff decide.</summary>
    public int? SuggestedRefundPercentage { get; set; }

    /// <summary>Number of stored Agora rows whose payload could be parsed into the timeline.</summary>
    public int EventCount { get; set; }

    /// <summary>Largest gap between when Agora says an event happened and when we stored it.</summary>
    public int MaxIngestLagSeconds { get; set; }

    // ── Heartbeat cross-check ────────────────────────────────────────────────
    // Our own client-side beats, reduced the same way as the Agora numbers above so the two
    // sources can be compared line by line. They agree in a normal lesson; a disagreement is
    // itself the finding, which is why both are reported instead of one merged figure.

    public int TutorHeartbeatSeconds { get; set; }
    public int StudentHeartbeatSeconds { get; set; }

    /// <summary>Seconds both sides were beating at the same time.</summary>
    public int HeartbeatOverlapSeconds { get; set; }

    /// <summary>HeartbeatOverlapSeconds / ScheduledSeconds, rounded to 4 decimals.</summary>
    public double HeartbeatOverlapRatio { get; set; }

    /// <summary>Beats received across every participant, 0 when the lesson predates this capture.</summary>
    public int HeartbeatCount { get; set; }

    // ── Punctuality ──────────────────────────────────────────────────────────

    /// <summary>
    /// Seconds the tutor arrived after the scheduled start, past the configured grace period.
    /// Null when no arrival could be established at all; 0 means on time.
    /// </summary>
    public int? TutorLateSeconds { get; set; }

    /// <summary>
    /// Seconds between the tutor's last departure and the scheduled end, past the grace period.
    /// Null when the lesson is still running or no departure is known; 0 means they stayed.
    /// </summary>
    public int? TutorEarlyLeaveSeconds { get; set; }

    /// <summary>
    /// Which source the punctuality numbers came from: <c>agora</c> (server-side, strong),
    /// <c>heartbeat</c> (our own client, weaker) or null when neither had usable presence.
    /// </summary>
    public string? PunctualitySource { get; set; }
}

/// <summary>
/// One participant's heartbeat chain. Weaker evidence than Agora's channel events — it comes from
/// the user's own browser — but it is the only source that can tell a room left open apart from a
/// lesson being taught, and it still stands when Agora sent us nothing.
/// </summary>
public class SessionLogHeartbeat
{
    public string AppUserId { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string? DisplayName { get; set; }

    public DateTime? FirstBeatAt { get; set; }
    public DateTime? LastBeatAt { get; set; }

    /// <summary>Seconds covered by the beat runs, gaps excluded.</summary>
    public int TotalSeconds { get; set; }

    public int BeatCount { get; set; }

    /// <summary>Runs of uninterrupted beating. More than one means reporting stopped and resumed.</summary>
    public int RunCount { get; set; }

    /// <summary>Runs that ended because beats simply stopped — a crash, a closed tab or lost network.</summary>
    public int GapCount { get; set; }

    /// <summary>True when beats are still arriving for an ongoing lesson.</summary>
    public bool IsCurrentlyBeating { get; set; }

    // ── Client-reported activity ─────────────────────────────────────────────

    /// <summary>Beats that carried activity fields. Zero means activity is unknown, not absent.</summary>
    public int ReportedBeats { get; set; }

    public int MicOnBeats { get; set; }
    public int CameraOnBeats { get; set; }
    public int IdleBeats { get; set; }

    /// <summary>
    /// Share of reported beats with no microphone, no camera and an idle tab. Null when nothing was
    /// reported, so "unknown" can never be rendered as 0%.
    /// </summary>
    public double? IdleRatio { get; set; }

    public List<SessionLogHeartbeatRun> Runs { get; set; } = [];
}

/// <summary>A run of consecutive heartbeats with no gap longer than the presence window.</summary>
public class SessionLogHeartbeatRun
{
    public DateTime StartedAt { get; set; }
    public DateTime LastBeatAt { get; set; }
    public int BeatCount { get; set; }

    public int ReportedBeats { get; set; }
    public int MicOnBeats { get; set; }
    public int CameraOnBeats { get; set; }
    public int IdleBeats { get; set; }

    /// <summary>Null while still open; otherwise <see cref="PresenceIntervalCloseReasonLabels"/> input.</summary>
    public string? ClosedReason { get; set; }
    public string? ClosedReasonLabel { get; set; }
}

/// <summary>
/// A network and device one participant was admitted from. Two entries for the same user in one
/// lesson mean the account entered from two places — worth a look, not a verdict on its own.
/// </summary>
public class SessionLogDeviceUse
{
    public string AppUserId { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string? DisplayName { get; set; }

    /// <summary>Empty when the address could not be resolved for this admission.</summary>
    public string IpAddress { get; set; } = string.Empty;

    public string DeviceLabel { get; set; } = string.Empty;
    public string UserAgent { get; set; } = string.Empty;

    public DateTime FirstSeenAt { get; set; }
    public DateTime LastSeenAt { get; set; }
    public int AdmissionCount { get; set; }
}

public class SessionLogParticipant
{
    public string? AppUserId { get; set; }
    public string Role { get; set; } = string.Empty;
    public string? DisplayName { get; set; }

    /// <summary>Raw uid as Agora reported it, kept as text because it may be numeric or a string.</summary>
    public string? AgoraUid { get; set; }

    /// <summary>"exact" when the uid matched our account id, "correlated" when it was matched by
    /// admission time, "unmatched" when no binding was possible.</summary>
    public string IdentityConfidence { get; set; } = SessionLogIdentityConfidence.Unmatched;

    public DateTime? FirstJoinAt { get; set; }
    public DateTime? LastLeaveAt { get; set; }

    public int TotalSeconds { get; set; }
    public int JoinCount { get; set; }

    /// <summary>True when the last processed event is an open join in an ongoing lesson.</summary>
    public bool IsCurrentlyPresent { get; set; }

    /// <summary>Leaves that were not the user's own choice (network timeouts and reconnects).</summary>
    public int DropCount { get; set; }

    public string? Platform { get; set; }

    /// <summary>Merged presence windows, so the client can draw the timeline without recomputing.</summary>
    public List<SessionLogInterval> Intervals { get; set; } = [];

    public List<SessionLogDisconnect> Disconnects { get; set; } = [];
}

/// <summary>A continuous window during which a participant was inside the channel.</summary>
public record SessionLogInterval(DateTime Start, DateTime End);

public class SessionLogDisconnect
{
    public DateTime At { get; set; }
    public int? Reason { get; set; }
    public string ReasonLabel { get; set; } = string.Empty;
    public bool Involuntary { get; set; }

    /// <summary>Seconds the user had been in the channel before this leave, as reported by Agora.</summary>
    public int? SessionDurationSeconds { get; set; }
}

public class SessionLogEvent
{
    public DateTime EventAt { get; set; }
    public DateTime ReceivedAt { get; set; }
    public short EventType { get; set; }
    public string EventLabel { get; set; } = string.Empty;

    public string? AgoraUid { get; set; }
    public string? AgoraAccount { get; set; }
    public string? AppUserId { get; set; }
    public string? Role { get; set; }
    public string? DisplayName { get; set; }

    public long? ClientSequence { get; set; }
    public int? ClientType { get; set; }
    public int? Platform { get; set; }
    public string? PlatformLabel { get; set; }

    public int? Reason { get; set; }
    public string? ReasonLabel { get; set; }

    public int? DurationSeconds { get; set; }
}

/// <summary>Which evidence source the punctuality numbers were derived from.</summary>
public static class SessionLogPunctualitySource
{
    /// <summary>Agora's server-side channel events — a clock the participant does not control.</summary>
    public const string Agora = "agora";

    /// <summary>Our own classroom heartbeat, used only when Agora reported nothing.</summary>
    public const string Heartbeat = "heartbeat";
}

public static class SessionLogIdentityConfidence
{
    public const string Exact = "exact";
    public const string Correlated = "correlated";
    public const string Unmatched = "unmatched";
}

/// <summary>
/// Warnings surfaced to staff. These exist so a missing signal is never read as evidence of
/// absence — "we have no data" and "nobody showed up" must not look the same on screen.
/// </summary>
public static class SessionLogFlag
{
    /// <summary>Agora sent nothing for this session. Absence of data, NOT proof nobody joined.</summary>
    public const string NoAgoraData = "no_agora_data";

    /// <summary>At least one participant event could not be parsed or had no uid.</summary>
    public const string InsufficientEvidence = "insufficient_evidence";

    /// <summary>Session predates the participant registry, so uids can only be matched exactly.</summary>
    public const string NoParticipantRegistry = "no_participant_registry";

    /// <summary>At least one uid was bound to a user by timing rather than an exact id match.</summary>
    public const string IdentityUncertain = "identity_uncertain";

    /// <summary>A join never received its matching leave; the interval was closed artificially.</summary>
    public const string UnclosedInterval = "unclosed_interval";

    /// <summary>Tutor and student were never in the room together.</summary>
    public const string ZeroOverlap = "zero_overlap";

    public const string TutorNeverJoined = "tutor_never_joined";
    public const string StudentNeverJoined = "student_never_joined";

    /// <summary>Someone was dropped by the network rather than leaving on purpose.</summary>
    public const string NetworkDrop = "network_drop";

    /// <summary>A disconnect caused by our own token handling — the user is not at fault.</summary>
    public const string TokenError = "token_error";

    /// <summary>Agora flagged abnormal join/leave churn.</summary>
    public const string UnusualActivity = "unusual_activity";

    /// <summary>The Agora cloud recorder appeared in the channel; it is excluded from attendance.</summary>
    public const string RecorderPresent = "recorder_present";

    /// <summary>Our heartbeat check-in disagrees with what Agora recorded.</summary>
    public const string CheckInMismatch = "check_in_mismatch";

    /// <summary>
    /// Our client kept beating but Agora reported no presence at all. Usually means notifications
    /// are not reaching us — read it as a gap in the Agora feed, not as an absent participant.
    /// </summary>
    public const string PresenceWithoutAgora = "presence_without_agora";

    /// <summary>
    /// Someone was connected but their client reported no microphone, no camera and an idle tab for
    /// most of the lesson — the "opened the room and walked away" signal. Client-reported.
    /// </summary>
    public const string IdlePresence = "idle_presence";

    /// <summary>No beat carried activity fields, so "was anyone teaching" cannot be answered.</summary>
    public const string NoActivityReports = "no_activity_reports";

    /// <summary>The same account was admitted from more than one device in this lesson.</summary>
    public const string MultipleDevices = "multiple_devices";

    /// <summary>
    /// The same account was admitted from more than one network address. Suspicious for account
    /// sharing, but a mobile carrier rotating addresses mid-lesson looks identical.
    /// </summary>
    public const string MultipleNetworks = "multiple_networks";

    /// <summary>This lesson has no device/network record at all — it predates that capture.</summary>
    public const string NoDeviceRecord = "no_device_record";
}

/// <summary>Vietnamese labels for <see cref="MV.DomainLayer.Constants.PresenceIntervalCloseReason"/>.</summary>
public static class PresenceIntervalCloseReasonLabels
{
    public static string? Label(string? closedReason) => closedReason switch
    {
        null => null,
        MV.DomainLayer.Constants.PresenceIntervalCloseReason.Leave => "Client báo rời phòng",
        MV.DomainLayer.Constants.PresenceIntervalCloseReason.Gap => "Ngừng gửi nhịp (đóng tab, treo máy hoặc mất mạng)",
        _ => closedReason
    };
}
