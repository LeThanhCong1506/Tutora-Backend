namespace MV.DomainLayer.DTO.ResponseModel;

/// <summary>
/// Thống kê tranh chấp cho admin dashboard
/// </summary>
public class DisputeStatsResponse
{
    public int TotalPending { get; set; }
    public int TotalInvestigating { get; set; }
    public int ResolvedThisMonth { get; set; }
    public decimal TotalRefundedThisMonth { get; set; }
}
