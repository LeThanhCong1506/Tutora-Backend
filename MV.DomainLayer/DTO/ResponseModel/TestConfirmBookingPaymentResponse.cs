namespace MV.DomainLayer.DTO.ResponseModel;

public class TestConfirmBookingPaymentResponse
{
    public int BookingId { get; set; }
    public string Phase { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string TransactionId { get; set; } = string.Empty;
}
