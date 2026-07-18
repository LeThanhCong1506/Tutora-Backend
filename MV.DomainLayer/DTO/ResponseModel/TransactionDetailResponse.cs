namespace MV.DomainLayer.DTO.ResponseModel;

/// <summary>
/// Chi tiết một giao dịch ví, (booking / dispute / withdrawal). 
/// </summary>
public class TransactionDetailResponse
{
    // Thông tin chung của giao dịch
    public int TransactionId { get; set; }
    public decimal Amount { get; set; }
    public string TransactionType { get; set; } = "";
    public string Description { get; set; } = "";
    public int? ReferenceId { get; set; }
    public string? ReferenceTable { get; set; }
    public DateTime CreatedAt { get; set; }

    // Hoá đơn booking (referenceTable = "booking")
    public BookingInvoiceDetail? Booking { get; set; }

    // Tranh chấp (referenceTable = "dispute")
    public DisputeTransactionDetail? Dispute { get; set; }

    // Rút tiền (referenceTable = "withdrawal")
    public WithdrawalTransactionDetail? Withdrawal { get; set; }
}

public class BookingInvoiceDetail
{
    public int BookingId { get; set; }
    public string? PaymentCode { get; set; }
    public string? SubjectName { get; set; }
    public string? TutorName { get; set; }
    public string? StudentName { get; set; }
    public int? TotalSessions { get; set; }
    public decimal? PricePerHour { get; set; }
    public decimal? TotalAmount { get; set; }
    public decimal? DepositAmount { get; set; }
    public decimal? RemainingAmount { get; set; }
    public string? Status { get; set; }
    public string? PaymentStatus { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? CreatedAt { get; set; }
}

public class DisputeTransactionDetail
{
    public int DisputeId { get; set; }
    public int? BookingId { get; set; }
    public string? DisputeType { get; set; }
    public string? Reason { get; set; }
    public string? Status { get; set; }
    public string? ResolutionNote { get; set; }
    public decimal? RefundAmount { get; set; }
    public int? RefundPercentage { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
}

public class WithdrawalTransactionDetail
{
    public int WithdrawalId { get; set; }
    public decimal Amount { get; set; }
    public string? Status { get; set; }
    public string? BankName { get; set; }
    public string? AccountNumber { get; set; }
    public string? AccountHolderName { get; set; }
    public DateTime? RequestedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public string? CompletionNote { get; set; }
}
