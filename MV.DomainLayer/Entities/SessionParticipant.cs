using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MV.DomainLayer.Entities;

/// <summary>
/// A user admitted into a live session, with the moment admission happened.
/// The session log uses <see cref="FirstAdmittedAt"/> to bind an Agora channel uid to a real user
/// when the notification payload carries Agora's internal numeric id instead of our account string.
/// </summary>
[Table("session_participants")]
public class SessionParticipant
{
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

    [Column("first_admitted_at")]
    public DateTime FirstAdmittedAt { get; set; }

    [Column("last_admitted_at")]
    public DateTime LastAdmittedAt { get; set; }

    /// <summary>How many times this user was admitted (rejoins, device takeovers).</summary>
    [Column("admission_count")]
    public int AdmissionCount { get; set; }
}
