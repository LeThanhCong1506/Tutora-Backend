namespace MV.DomainLayer.Entities;

/// <summary>
/// Số credit AI được cấp, có hạn dùng riêng.
/// </summary>
public partial class AiCreditBatch
{
    public Guid Id { get; set; }

    public string UserId { get; set; } = null!;

    /// <summary>free_signup | booking_bonus | purchase.</summary>
    public string Source { get; set; } = null!;

    /// <summary>Khoá chống cấp trùng: 'free:&lt;userId&gt;', 'booking:&lt;id&gt;'...</summary>
    public string? ReferenceId { get; set; }

    public int Granted { get; set; }

    public int Consumed { get; set; }

    public DateTime GrantedAt { get; set; }

    /// <summary>NULL = không hết hạn.</summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>Còn dùng được bao nhiêu của lô này.</summary>
    public int Remaining => Granted - Consumed;
}
