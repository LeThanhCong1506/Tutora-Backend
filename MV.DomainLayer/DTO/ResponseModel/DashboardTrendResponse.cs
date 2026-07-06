namespace MV.DomainLayer.DTO.ResponseModel
{
    public class DashboardTrendResponse
    {
        public string From { get; set; } = string.Empty;
        public string To { get; set; } = string.Empty;
        public string Bucket { get; set; } = string.Empty;

        public List<FinancialTrendPoint> FinancialTrend { get; set; } = [];
        public List<ClassSessionTrendPoint> ClassSessionTrend { get; set; } = [];
        public ClassSessionRates ClassSessionRates { get; set; } = new();
    }

    public class FinancialTrendPoint
    {
        public string Label { get; set; } = string.Empty;
        public decimal Gmv { get; set; }
        public decimal PlatformRevenue { get; set; }
    }

    public class ClassSessionTrendPoint
    {
        public string Label { get; set; } = string.Empty;
        public int Completed { get; set; }
        public int Cancelled { get; set; }
        public int NoShow { get; set; }
    }

    public class ClassSessionRates
    {
        public double CompletionRate { get; set; }
        public double CancellationRate { get; set; }
        public double NoShowRate { get; set; }
    }
}
