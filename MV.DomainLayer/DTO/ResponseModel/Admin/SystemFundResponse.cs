namespace MV.DomainLayer.DTO.ResponseModel.Admin;

/// <summary>Số dư hiện tại của quỹ hệ thống.</summary>
public class SystemFundResponse
{
    public decimal Balance { get; set; }
    public DateTime UpdatedAt { get; set; }
}
