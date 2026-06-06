namespace MV.DomainLayer.Settings;

public class FraudDetectionSettings
{
    public const string SectionName = "FraudDetection";

    public int MaxWithdrawalsPerDay { get; set; } = 2;
    public int MaxWithdrawalsPerWeek { get; set; } = 5;
    public int MaxWithdrawalsPerMonth { get; set; } = 15;

    public decimal MaxAmountPerTransaction { get; set; } = 10_000_000m;
    public decimal MaxAmountPerDay { get; set; } = 30_000_000m;
    public decimal MaxAmountPerMonth { get; set; } = 100_000_000m;

    public int NewAccountBlockDays { get; set; } = 7;
    public int NewAccountRestrictDays { get; set; } = 30;
    public decimal NewAccountMaxAmount { get; set; } = 2_000_000m;

    public int BankChangeCooldownHours { get; set; } = 24;
    public int MaxBankChangesPerMonth { get; set; } = 2;

    public int MinCompletedLessons { get; set; } = 1;

    public decimal FullBalanceThresholdPercent { get; set; } = 95m;
    public int MultipleIpThreshold { get; set; } = 3;
    public int IpPatternLookbackDays { get; set; } = 7;
    public decimal UnusualAmountMultiplier { get; set; } = 3m;
    public int MinWithdrawalsForAverage { get; set; } = 3;

    public int CacheTtlHours { get; set; } = 24;
    public int FraudCheckTimeoutSeconds { get; set; } = 30;
}
