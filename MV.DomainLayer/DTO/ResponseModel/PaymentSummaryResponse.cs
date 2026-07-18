namespace MV.DomainLayer.DTO.ResponseModel;

/// <summary>
/// Lightweight snapshot of a booking's current unpaid phase for the payment screen.
/// Unlike <see cref="PaymentInfoResponse"/> this does NOT create a PayOS payment
/// request/link — it only reads amounts and wallet balance so the parent can pick a
/// payment method first. The PayOS link is created lazily only when the parent
/// actually chooses bank transfer (via GetPaymentInfoAsync).
/// </summary>
public class PaymentSummaryResponse
{
    public int BookingId { get; set; }
    public decimal Amount { get; set; }
    public string PaymentPhase { get; set; } = MV.DomainLayer.Constants.PaymentPhase.Deposit;
    public decimal TotalAmount { get; set; }
    public decimal DepositAmount { get; set; }
    public decimal RemainingAmount { get; set; }
    public bool IsDepositPaid { get; set; }
    public bool IsRemainingPaid { get; set; }
    public decimal WalletBalance { get; set; }
    public bool CanPayWithWallet { get; set; }
    public DateTime? ExpiredAt { get; set; }
}
