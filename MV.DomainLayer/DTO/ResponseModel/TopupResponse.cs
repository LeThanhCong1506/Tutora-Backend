using MV.DomainLayer.Constants;

namespace MV.DomainLayer.DTO.ResponseModel;

public class TopupResponse
{
    public string PaymentLinkId { get; set; } = "";
    public long OrderCode { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = MV.DomainLayer.Constants.Currency.Vnd;
    public string CheckoutUrl { get; set; } = "";
    public string QrCode { get; set; } = "";
    public string AccountNumber { get; set; } = "";
    public string AccountName { get; set; } = "";
    public string Bin { get; set; } = "";
    public string Description { get; set; } = "";
    public DateTime? ExpiredAt { get; set; }
}
