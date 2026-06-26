namespace MV.DomainLayer.DTO.ResponseModel;

/// <summary>
/// Payment status snapshot for a booking — two-phase (deposit + remaining).
/// Caller: <c>IPaymentService.GetPaymentStatusAsync</c>
///   → <c>GET /api/payment/status/{bookingId}</c>.
/// </summary>
public class PaymentStatusResponse
{
    public int BookingId { get; set; }
    public string Status { get; set; } = "";
    public int Amount { get; set; }
    public int AmountPaid { get; set; }
    public int AmountRemaining { get; set; }
    public bool IsPaid { get; set; }
    public decimal DepositAmount { get; set; }
    public decimal RemainingAmount { get; set; }
    public bool IsDepositPaid { get; set; }
    public bool IsRemainingPaid { get; set; }
    public bool IsExpired { get; set; }
    public bool RefundedToWallet { get; set; }
    public decimal RefundAmount { get; set; }
}
