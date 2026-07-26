using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MV.DomainLayer.Entities;

/// <summary>
/// One authenticated SignalR connection that reached a lesson's waiting lobby.
///
/// The lobby client refreshes state periodically, so <see cref="LastSeenAt"/> and
/// <see cref="BeatCount"/> prove that the participant remained in the waiting flow instead of
/// merely opening the page. Reconnects intentionally create separate rows.
/// </summary>
[Table("session_lobby_visits")]
public class SessionLobbyVisit
{
    [Key]
    [Column("lobby_visit_id")]
    public long LobbyVisitId { get; set; }

    [Column("class_session_id")]
    public int ClassSessionId { get; set; }

    [Required]
    [MaxLength(50)]
    [Column("app_user_id")]
    public string AppUserId { get; set; } = null!;

    [Required]
    [MaxLength(20)]
    [Column("role")]
    public string Role { get; set; } = null!;

    /// <summary>Opaque SignalR connection id, used only to close/update the matching visit.</summary>
    [Required]
    [MaxLength(128)]
    [Column("connection_id")]
    public string ConnectionId { get; set; } = null!;

    [Column("entered_at")]
    public DateTime EnteredAt { get; set; }

    [Column("last_seen_at")]
    public DateTime LastSeenAt { get; set; }

    [Column("beat_count")]
    public int BeatCount { get; set; }

    [Column("left_at")]
    public DateTime? LeftAt { get; set; }

    [MaxLength(20)]
    [Column("closed_reason")]
    public string? ClosedReason { get; set; }
}
