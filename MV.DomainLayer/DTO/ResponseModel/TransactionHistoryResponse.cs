namespace MV.DomainLayer.DTO.ResponseModel;

public class TransactionHistoryResponse
{
    public int TransactionId { get; set; }
    public decimal Amount { get; set; }
    public string TransactionType { get; set; } = "";
    public string Description { get; set; } = "";
    public int? ReferenceId { get; set; }
    public string? ReferenceTable { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? ProviderTransactionId { get; set; }
    public DateTime? PaidAt { get; set; }
    public string? ProofImageUrl { get; set; }
}

public class TransactionHistoryPagedResponse
{
    public List<TransactionHistoryResponse> Transactions { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}
