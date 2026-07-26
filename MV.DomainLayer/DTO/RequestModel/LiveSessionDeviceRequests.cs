using System.ComponentModel.DataAnnotations;

namespace MV.DomainLayer.DTO.RequestModel;

public class LiveSessionJoinRequest
{
    [Required]
    public string ParticipationId { get; set; } = string.Empty;

    [Required]
    public string DeviceId { get; set; } = string.Empty;

    [Required]
    [MaxLength(120)]
    public string DeviceLabel { get; set; } = string.Empty;
}

public sealed class LiveSessionTakeoverRequest : LiveSessionJoinRequest
{
    [Required]
    public string ExpectedActiveLeaseId { get; set; } = string.Empty;
}

public sealed class LiveSessionLeaseRequest
{
    [Required]
    public string ParticipationId { get; set; } = string.Empty;

    [Required]
    public string LeaseId { get; set; } = string.Empty;

    /// <summary>
    /// What the classroom was actually doing at this beat. Optional so a client that predates the
    /// field keeps working — when it is absent the session log records the beat as present with
    /// unknown activity, which is not the same as idle.
    /// </summary>
    public LiveSessionActivityReport? Activity { get; set; }
}

/// <summary>
/// Client-reported classroom activity, sent with each heartbeat.
///
/// Self-reported and therefore weaker evidence than Agora's own channel events — a determined
/// tutor could fake it. It exists to answer the one question Agora cannot ("was anyone actually
/// teaching, or was the room just left open"), and the session log labels it as client-reported
/// wherever it is shown.
/// </summary>
public sealed class LiveSessionActivityReport
{
    /// <summary>True when the local microphone track is published and unmuted.</summary>
    public bool MicOn { get; set; }

    /// <summary>True when the local camera track is published and unmuted.</summary>
    public bool CameraOn { get; set; }

    /// <summary>True when the classroom tab is hidden or has seen no interaction for a while.</summary>
    public bool Idle { get; set; }
}
