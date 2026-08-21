namespace MV.DomainLayer.DTO.ResponseModel.Admin;

public class AdminWithdrawalLimitResponse
{
    /// <summary>Đơn vị: VND.</summary>
    public decimal MinWithdrawalAmount { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedByName { get; set; }
}
