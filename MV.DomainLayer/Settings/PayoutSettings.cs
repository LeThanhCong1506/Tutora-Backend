namespace MV.DomainLayer.Settings;

public class PayoutSettings
{
    public const string SectionName = "Payout";

    // Retry configuration
    public int MaxRetries { get; set; } = 3;
    public int RetryDelayMinutes { get; set; } = 5;
    public int RetryDelayMultiplier { get; set; } = 2; // Exponential backoff: 5min, 10min, 20min

    // Balance monitoring thresholds (in VND)
    public decimal BalanceWarningThreshold { get; set; } = 10_000_000m; // 10M VND
    public decimal BalanceCriticalThreshold { get; set; } = 5_000_000m; // 5M VND
    public int BalanceCacheDurationMinutes { get; set; } = 5;

    // Background job intervals (in minutes)
    public int ProcessingIntervalMinutes { get; set; } = 5;
    public int BalanceMonitoringIntervalMinutes { get; set; } = 30;
    public int StatusPollingIntervalMinutes { get; set; } = 10;

    // Processing configuration
    public int MaxConcurrentPayouts { get; set; } = 10;
    public int PayoutDelayMilliseconds { get; set; } = 1000; // 1 second delay between payouts

    // Special retry delays (in minutes)
    public int InsufficientBalanceRetryDelayMinutes { get; set; } = 30;

    // Alert deduplication (in minutes)
    public int AlertDeduplicationWindowMinutes { get; set; } = 60;
}
