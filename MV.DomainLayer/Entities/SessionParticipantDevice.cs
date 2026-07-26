using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MV.DomainLayer.Entities;

/// <summary>
/// One network and device a participant was admitted from, per lesson.
///
/// A user normally produces exactly one row. Two rows for the same user in one lesson mean the
/// account entered the room from two places — the signal behind "account sharing" and "logged in
/// from several devices". It stays evidence rather than a verdict: mobile carriers rotate
/// addresses mid-lesson, and a legitimate device switch also creates a second row.
/// </summary>
[Table("session_participant_devices")]
public class SessionParticipantDevice
{
    [Key]
    [Column("device_row_id")]
    public long DeviceRowId { get; set; }

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

    /// <summary>
    /// Empty when the address could not be resolved. Behind a reverse proxy this is the proxy
    /// unless <c>SessionEvidence:TrustForwardedFor</c> is enabled.
    /// </summary>
    [Required]
    [MaxLength(45)]
    [Column("ip_address")]
    public string IpAddress { get; set; } = string.Empty;

    /// <summary>Client-generated stable device identifier from the live-session device lease.</summary>
    [Required]
    [MaxLength(100)]
    [Column("device_id")]
    public string DeviceId { get; set; } = string.Empty;

    [Required]
    [MaxLength(120)]
    [Column("device_label")]
    public string DeviceLabel { get; set; } = string.Empty;

    [Required]
    [MaxLength(400)]
    [Column("user_agent")]
    public string UserAgent { get; set; } = string.Empty;

    [Column("first_seen_at")]
    public DateTime FirstSeenAt { get; set; }

    [Column("last_seen_at")]
    public DateTime LastSeenAt { get; set; }

    /// <summary>Admissions seen from this network and device, token renewals included.</summary>
    [Column("admission_count")]
    public int AdmissionCount { get; set; }
}
