namespace MV.DomainLayer.DTO.ResponseModel.Admin;

/// <summary>
/// Top-level response for GET /api/admin/dashboard/disputes
/// </summary>
public class AdminDisputeStatsResponse
{
    public DisputeStatsOverview Overview { get; set; } = new();
    public DisputeStatsFinancial Financial { get; set; } = new();
    public List<DisputeTypeCount> ByType { get; set; } = new();
    public List<DisputeTrendItem> Trend { get; set; } = new();
    public DateTime? FilterFrom { get; set; }
    public DateTime? FilterTo { get; set; }
}

public class DisputeStatsOverview
{
    public int TotalDisputes { get; set; }
    public int Pending { get; set; }
    public int Investigating { get; set; }
    public int Resolved { get; set; }
    public int Closed { get; set; }
    public decimal? ResolutionRatePercent { get; set; }
    public decimal? AvgResolutionDays { get; set; }
}

public class DisputeStatsFinancial
{
    public decimal TotalRefundAmount { get; set; }
    public int RefundsThisPeriod { get; set; }
    public decimal RefundAmountThisPeriod { get; set; }
}

public class DisputeTypeCount
{
    public string Type { get; set; } = string.Empty;
    public int Count { get; set; }
}

public class DisputeTrendItem
{
    public string Month { get; set; } = string.Empty;
    public int Count { get; set; }
    public decimal RefundAmount { get; set; }
}
