namespace MV.DomainLayer.Settings;

public class TrustScoringSettings
{
    public const string SectionName = "TrustScoring";

    // Base score
    public int BaseScore { get; set; } = 50;

    // Positive factor points
    public int AccountAge90PlusPoints { get; set; } = 20;
    public int AccountAge30To90Points { get; set; } = 15;
    public int CompletedLessons10PlusPoints { get; set; } = 20;
    public int CompletedLessons5To10Points { get; set; } = 10;
    public int BankVerifiedPoints { get; set; } = 20;
    public int NoDisputesPoints { get; set; } = 20;
    public int SmallAmountPoints { get; set; } = 10;
    public int HasSuccessfulWithdrawalPoints { get; set; } = 5;

    // Negative factor points
    public int NewAccountPenalty { get; set; } = 30;
    public int BankChangedRecentlyPenalty { get; set; } = 20;
    public int MultipleIpsPenalty { get; set; } = 15;
    public int FirstWithdrawalPenalty { get; set; } = 10;
    public int FullBalanceWithdrawalPenalty { get; set; } = 10;
    public int LargeAmountPenalty { get; set; } = 5;

    // Thresholds
    public int AccountAge90DaysThreshold { get; set; } = 90;
    public int AccountAge30DaysThreshold { get; set; } = 30;
    public int CompletedLessons10Threshold { get; set; } = 10;
    public int CompletedLessons5Threshold { get; set; } = 5;
    public decimal SmallAmountThreshold { get; set; } = 2_000_000m;
    public decimal LargeAmountThreshold { get; set; } = 5_000_000m;
    public int BankChangedRecentlyDays { get; set; } = 30;

    // Decision thresholds
    public int AutoApproveThreshold { get; set; } = 50;
    public int ManualReviewThreshold { get; set; } = 30;

    // Processing times
    public int ManualReviewProcessingHours { get; set; } = 24;
}
