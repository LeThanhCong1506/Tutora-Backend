namespace MV.DomainLayer.DTO.ResponseModel;

public class EscrowStatusItemResponse
{
    public int BookingId { get; set; }
    public string ParentName { get; set; } = string.Empty;
    public string StudentName { get; set; } = string.Empty;
    public string? SubjectName { get; set; }
    public string? BookingStatus { get; set; }
    public decimal HeldAmount { get; set; }
}

public class EscrowStatusResponse
{
    public List<EscrowStatusItemResponse> Items { get; set; } = new();
    public decimal TotalHeld { get; set; }
}
