namespace MV.DomainLayer.DTO.RequestModel;

public class AdminConfirmPaymentRequest
{
    public decimal Amount { get; set; }
    public string? TransactionId { get; set; }
    public DateTime? PaidAt { get; set; }
}
