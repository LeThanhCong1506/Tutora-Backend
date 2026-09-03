namespace MV.DomainLayer.DTO.ResponseModel;

/// <summary>
/// Settlement result after classSession confirmation
/// </summary>
public class SettlementResultResponse
{
    public int ClassSessionId { get; set; }
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
