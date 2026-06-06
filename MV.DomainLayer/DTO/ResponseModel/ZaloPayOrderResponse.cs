namespace MV.DomainLayer.DTO.ResponseModel;

public class ZaloPayOrderResponse
{
    public string AppTransId { get; set; } = string.Empty;
    public long Amount { get; set; }
    public string Description { get; set; } = string.Empty;
}
