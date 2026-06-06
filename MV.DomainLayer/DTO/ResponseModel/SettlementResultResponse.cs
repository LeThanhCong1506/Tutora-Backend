namespace MV.DomainLayer.DTO.ResponseModel;

/// <summary>
/// Settlement result after lesson confirmation
/// </summary>
public class SettlementResultResponse
{
    public int LessonId { get; set; }
    public int? BookingId { get; set; }

    public bool Success { get; set; }
    public string? Message { get; set; }

    /// <summary>
    /// Amount released to tutor
    /// </summary>
    public decimal AmountReleased { get; set; }

    /// <summary>
    /// Amount refunded to parent (if partial refund)
    /// </summary>
    public decimal AmountRefunded { get; set; }

    /// <summary>
    /// Settlement type: full_release, partial_refund, full_refund
    /// </summary>
    public string? SettlementType { get; set; }

    /// <summary>
    /// Transaction ID for the release
    /// </summary>
    public long? TransactionId { get; set; }

    /// <summary>
    /// New tutor wallet balance
    /// </summary>
    public decimal? NewTutorBalance { get; set; }

    /// <summary>
    /// Remaining sessions in booking
    /// </summary>
    public int? SessionsRemaining { get; set; }
}

/// <summary>
/// No-show action result
/// </summary>
public class NoShowActionResultResponse
{
    public int LessonId { get; set; }
    public string ActionType { get; set; } = null!;
    public bool Success { get; set; }
    public string? Message { get; set; }

    /// <summary>
    /// Amount refunded (for free_session or change_tutor)
    /// </summary>
    public decimal? AmountRefunded { get; set; }

    /// <summary>
    /// New makeup lesson ID (for makeup action)
    /// </summary>
    public int? MakeupLessonId { get; set; }

    /// <summary>
    /// Was tutor warning created
    /// </summary>
    public bool WarningCreated { get; set; }
}
