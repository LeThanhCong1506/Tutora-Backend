namespace MV.DomainLayer.DTO.ResponseModel.Admin;

/// <summary>
/// Response for GET /api/admin/financials/transactions
/// </summary>
public class AdminTransactionListResponse
{
    public List<AdminTransactionItem> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    /// <summary>Sum of Amount for the full filtered result set (before paging)</summary>
    public decimal TotalAmount { get; set; }
}

public class AdminTransactionItem
{
    public int TransactionId { get; set; }
    public int? WalletId { get; set; }

    // Wallet owner
    public string? UserId { get; set; }
    public string? UserFullName { get; set; }
    public string? UserEmail { get; set; }
    public string? UserRole { get; set; }

    public decimal? Amount { get; set; }
    public string? TransactionType { get; set; }
    public string? Description { get; set; }

    public int? ReferenceId { get; set; }
    public string? ReferenceTable { get; set; }
    public long? OrderCode { get; set; }

    /// <summary>Created at in Vietnam time (UTC+7)</summary>
    public DateTime? CreatedAt { get; set; }
}
