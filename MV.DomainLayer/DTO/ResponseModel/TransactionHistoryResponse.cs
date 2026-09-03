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

    /// <summary>
    /// Lịch sử giao dịch là hợp nhất của hai sổ: wallet_transactions (biến động số dư ví) và
    /// payment_transactions (lệnh chi tay ra ngân hàng).
    /// </summary>
    public string Source { get; set; } = Constants.TransactionSource.Wallet;

    /// <summary>
    /// Hình thức nhận/chuyển tiền
    /// </summary>
    public string Channel { get; set; } = Constants.TransactionChannel.Wallet;

    /// <summary>
    /// True khi dòng này KHÔNG làm đổi số dư ví
    /// </summary>
    public bool IsInformational { get; set; }

    /// <summary>Ngân hàng đích của lệnh chi; null với giao dịch ví.</summary>
    public string? BankName { get; set; }

    /// <summary>Số tài khoản đích, đã che bớt; null với giao dịch ví.</summary>
    public string? AccountNumber { get; set; }

    /// <summary>Mã tham chiếu staff đọc từ biên lai ngân hàng; null với giao dịch ví.</summary>
    public string? BankTransactionCode { get; set; }
}

public class TransactionHistoryPagedResponse
{
    public List<TransactionHistoryResponse> Transactions { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}
