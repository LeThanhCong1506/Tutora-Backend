namespace MV.DomainLayer.DTO.ResponseModel;

public class WithdrawalListResponse
{
    public List<WithdrawalItem> Items { get; set; } = new();
    public int Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

public class WithdrawalItem
{
    public int WithdrawalId { get; set; }
    public string TutorId { get; set; } = string.Empty;
    public string TutorName { get; set; } = string.Empty;
    public string TutorEmail { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string BankName { get; set; } = string.Empty;
    public string AccountNumber { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime RequestedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
}

public class WithdrawalDetailResponse
{
    public int WithdrawalId { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? BankName { get; set; }
    public string? AccountNumber { get; set; }
    public string? AccountHolderName { get; set; }
    public DateTime RequestedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public DateTime? ClaimedAt { get; set; }
    public string? CompletionNote { get; set; }
    public string? RejectionReason { get; set; }
    public string? TransactionId { get; set; }
    /// <summary>
    /// Mã tham chiếu (liên ngân hàng qua Napas) do ngân hàng cấp cho lệnh chi này. Người
    /// nhận vốn đã đọc được mã này trên ảnh biên lai (ProofImageUrl); đưa ra dạng text để
    /// họ tra cứu với ngân hàng của mình mà không phải mở ảnh ra soi.
    /// </summary>
    public string? BankTransactionCode { get; set; }
    public DateTime? PaidAt { get; set; }
    public string? ProofImageUrl { get; set; }
}
