namespace MV.DomainLayer.DTO.ResponseModel;

public class ZaloPayBookingInfoResponse
{
    public int BookingId { get; set; }
    public decimal Amount { get; set; }
    public string? SubjectName { get; set; }
    public string? ParentId { get; set; }
    public string? StudentId { get; set; }
}
