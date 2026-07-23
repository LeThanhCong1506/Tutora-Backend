using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MV.DomainLayer.Entities;

/// <summary>
/// An append-only copy of an Agora NCS RTC event used as independent session evidence.
/// Provider-specific fields remain in <see cref="Payload"/> until the probe establishes their shape.
/// </summary>
[Table("agora_channel_events")]
public class AgoraChannelEvent
{
    [Key]
    [Column("event_id")]
    public long EventId { get; set; }

    [Required]
    [MaxLength(64)]
    [Column("notice_id")]
    public string NoticeId { get; set; } = null!;

    [Column("class_session_id")]
    public int? ClassSessionId { get; set; }

    [Column("event_type")]
    public short EventType { get; set; }

    [Column("event_at")]
    public DateTime EventAt { get; set; }

    [Column("received_at")]
    public DateTime ReceivedAt { get; set; }

    [Required]
    [Column("payload", TypeName = "jsonb")]
    public string Payload { get; set; } = null!;
}
