namespace MV.DomainLayer.DTO.ResponseModel.Admin;

/// <summary>
/// Top-level response for GET /api/admin/dashboard/tutor-performance
/// </summary>
public class AdminTutorPerformanceResponse
{
    public decimal? PlatformAverageRating { get; set; }
    public decimal? PlatformAvgCompletionRate { get; set; }
    public List<TutorPerformanceItem> TopByRating { get; set; } = new();
    public List<TutorPerformanceItem> TopByLessonsCompleted { get; set; } = new();
    public List<TutorPerformanceItem> TopByRevenue { get; set; } = new();
    public TutorFeedbackSummary FeedbackSummary { get; set; } = new();
    public DateTime? FilterFrom { get; set; }
    public DateTime? FilterTo { get; set; }
}

public class TutorPerformanceItem
{
    public string TutorId { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public decimal? AverageRating { get; set; }
    public int TotalFeedbacks { get; set; }
    public int LessonsCompleted { get; set; }
    public int LessonsCancelled { get; set; }
    public int NoShows { get; set; }
    public decimal? CompletionRatePercent { get; set; }
    public decimal TotalRevenue { get; set; }
    public string? SubscriptionType { get; set; }
}

public class TutorFeedbackSummary
{
    public int TotalFeedbacks { get; set; }
    public decimal? AverageRating { get; set; }
    public List<RatingDistributionItem> RatingDistribution { get; set; } = new();
    public decimal? ParentSatisfactionRate { get; set; }
}

public class RatingDistributionItem
{
    public int Rating { get; set; }
    public int Count { get; set; }
}
