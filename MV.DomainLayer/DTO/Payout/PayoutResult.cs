namespace MV.DomainLayer.DTO.Payout;

public class PayoutResult
{
    public bool IsSuccess { get; set; }
    public string? PayosTransactionId { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public bool ShouldRetry { get; set; }
    public int? RetryDelayMinutes { get; set; }
}

public class PayoutStatusResult
{
    public string Status { get; set; } = null!;
    public string? TransactionId { get; set; }
    public decimal? Amount { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
}
