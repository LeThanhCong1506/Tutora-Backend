using System.Text.Json.Serialization;
using MV.DomainLayer.Constants;

namespace MV.DomainLayer.DTO.ResponseModel;

public class PaymentInfoResponse
{
    public int BookingId { get; set; }
    public string PaymentLinkId { get; set; } = "";
    public string PaymentCode { get; set; } = "";
    public decimal Amount { get; set; }
    public string Currency { get; set; } = MV.DomainLayer.Constants.Currency.Vnd;
    public string CheckoutUrl { get; set; } = "";
    public string QrCode { get; set; } = "";
    public string AccountNumber { get; set; } = "";
    public string AccountName { get; set; } = "";
    public string Bin { get; set; } = "";
    public string Description { get; set; } = "";
    public DateTime? ExpiredAt { get; set; }
    public string Status { get; set; } = "";
    public bool CanPayWithWallet { get; set; }
    public decimal WalletBalance { get; set; }
    public string PaymentPhase { get; set; } = MV.DomainLayer.Constants.PaymentPhase.Deposit;
    public decimal TotalAmount { get; set; }
    public decimal DepositAmount { get; set; }
    public decimal RemainingAmount { get; set; }
    public bool IsDepositPaid { get; set; }
    public bool IsRemainingPaid { get; set; }
}
