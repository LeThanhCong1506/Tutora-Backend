using MV.DomainLayer.Constants;

namespace MV.DomainLayer.DTO.ResponseModel.Admin;

public class PayoutOverviewResponse
{
    public TodayStatsResponse TodayStats { get; set; } = new();
    public ProcessingStatsResponse ProcessingStats { get; set; } = new();
    public FinancialStatsResponse FinancialStats { get; set; } = new();
    public DecisionBreakdownResponse DecisionBreakdown { get; set; } = new();
    public PayOSBalanceResponse? PayOSBalance { get; set; }
    public int RecentAlertsCount { get; set; }
}

public class TodayStatsResponse
{
    public int TotalRequests { get; set; }
    public int AutoApproved { get; set; }
    public int Delayed { get; set; }
    public int ManualReview { get; set; }
    public int Rejected { get; set; }
}

public class ProcessingStatsResponse
{
    public double AvgProcessingTime { get; set; } // in minutes
    public double SuccessRate { get; set; } // percentage
    public int PendingCount { get; set; }
}

public class FinancialStatsResponse
{
    public decimal TotalPayoutToday { get; set; }
    public decimal TotalPayoutThisMonth { get; set; }
}

public class DecisionBreakdownResponse
{
    public int AutoApprove { get; set; }
    public int Delayed { get; set; }
    public int ManualReview { get; set; }
    public int Rejected { get; set; }
}

public class PayOSBalanceResponse
{
    public decimal Balance { get; set; }
    public DateTime LastChecked { get; set; }
    public string AlertLevel { get; set; } = PayoutConstants.AlertSeverity.Normal; // normal, warning, critical
}
