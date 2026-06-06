namespace MV.DomainLayer.Interfaces;

/// <summary>
/// Interface for PayOS Payout API transfer operations
/// </summary>
public interface IPayOSTransferClient
{
    Task<TransferResponse> TransferAsync(TransferRequest request, CancellationToken cancellationToken = default);
    Task<PayoutStatusResponse> GetPayoutStatusAsync(string payoutId, CancellationToken cancellationToken = default);
}

public record TransferRequest
{
    public decimal Amount { get; init; }
    public string ToBin { get; init; } = string.Empty;
    public string ToAccountNumber { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string? Reference { get; init; }
}

public record TransferResponse
{
    public string TransactionId { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public string? Reference { get; init; }
    public bool IsSuccess { get; init; }
    public string? ErrorMessage { get; init; }
    public string? ErrorCode { get; init; }
}

public record PayoutStatusResponse
{
    public string Status { get; init; } = string.Empty;
    public string? TransactionId { get; init; }
    public decimal? Amount { get; init; }
    public bool IsSuccess { get; init; }
    public string? ErrorMessage { get; init; }
    public string? ErrorCode { get; init; }
}
