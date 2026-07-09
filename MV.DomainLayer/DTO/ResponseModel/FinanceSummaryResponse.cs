namespace MV.DomainLayer.DTO.ResponseModel;

public class FinanceSummaryResponse
{
    public decimal Balance { get; set; }
    public decimal AvailableBalance { get; set; }
    public decimal FrozenBalance { get; set; }
    public decimal TotalBalance { get; set; }
    public decimal TotalEarned { get; set; }
    public decimal PendingSettlement { get; set; }
    public DateTime? LastWithdrawalAt { get; set; }
}
