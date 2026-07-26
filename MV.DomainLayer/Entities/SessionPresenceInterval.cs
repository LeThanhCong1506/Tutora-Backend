using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MV.DomainLayer.Entities;

/// <summary>
/// A continuous run of presence heartbeats from one participant of a live session.
///
/// Agora's channel events say whether a user is connected to the media channel. They cannot say
/// whether that user is still at the desk, which is the difference between teaching a lesson and
/// opening the room and walking away. The classroom client beats every ~20 seconds and reports its
/// own microphone/camera state, and those beats are collapsed into this interval as they arrive.
/// </summary>
[Table("session_presence_intervals")]
public class SessionPresenceInterval
{
    [Key]
    [Column("interval_id")]
    public long IntervalId { get; set; }

    [Column("class_session_id")]
    public int ClassSessionId { get; set; }

    [Required]
    [MaxLength(50)]
    [Column("app_user_id")]
    public string AppUserId { get; set; } = null!;

    /// <summary>One of <see cref="MV.DomainLayer.Constants.SessionParticipantRole"/>.</summary>
    [Required]
    [MaxLength(20)]
    [Column("role")]
    public string Role { get; set; } = null!;

    [Column("started_at")]
    public DateTime StartedAt { get; set; }

    [Column("last_beat_at")]
    public DateTime LastBeatAt { get; set; }

    /// <summary>Beats received in this interval, so the chain stays auditable after collapsing.</summary>
    [Column("beat_count")]
    public int BeatCount { get; set; }

    /// <summary>
    /// Beats that carried the client activity fields. A client too old to send them must read as
    /// "unknown activity", never as "was idle", so every activity ratio is taken over this count.
    /// </summary>
    [Column("reported_beats")]
    public int ReportedBeats { get; set; }

    [Column("mic_on_beats")]
    public int MicOnBeats { get; set; }

    [Column("camera_on_beats")]
    public int CameraOnBeats { get; set; }

    /// <summary>Beats where the classroom tab was hidden or had seen no interaction for a while.</summary>
    [Column("idle_beats")]
    public int IdleBeats { get; set; }

    /// <summary>
    /// <c>null</c> while beats are still arriving, <c>leave</c> when the client left explicitly,
    /// <c>gap</c> when beats stopped for longer than the presence window.
    /// </summary>
    [MaxLength(20)]
    [Column("closed_reason")]
    public string? ClosedReason { get; set; }
}
