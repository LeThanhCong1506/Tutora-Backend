namespace MV.DomainLayer.DTO.ResponseModel;

/// <summary>
/// Feedback list for tutor profile (public)
/// </summary>
public class FeedbackListResponse
{
    public int FeedbackId { get; set; }
    public int? LessonId { get; set; }
    public int? BookingId { get; set; }

    public int Rating { get; set; }
    public string? Comment { get; set; }
    public string? FeedbackType { get; set; }

    public DateTime CreatedAt { get; set; }

    // From user info
    public string? ParentName { get; set; }
    public string? ParentAvatarUrl { get; set; }

    // Legacy compatibility
    public string? FromUserName => ParentName;
    public string? FromUserAvatarUrl => ParentAvatarUrl;

    // Subject info
    public string? SubjectName { get; set; }

    // Reply info
    public string? Reply { get; set; }
    public DateTime? RepliedAt { get; set; }

    // Legacy compatibility
    public string? ReplyComment => Reply;

    // Visibility (admin)
    public bool IsVisible { get; set; } = true;

    // Additional fields
    public string? InitialGoal { get; set; }
    public string? ActualResult { get; set; }
    public string? CourseDuration { get; set; }

    /// <summary>
    /// Star display (★★★★☆)
    /// </summary>
    public string RatingDisplay => new string('★', Rating) + new string('☆', 5 - Rating);

    /// <summary>
    /// Time since feedback
    /// </summary>
    public string? TimeSinceDisplay
    {
        get
        {
            var elapsed = MV.DomainLayer.Helpers.VietnamTimeHelper.Now - CreatedAt;
            if (elapsed.TotalDays >= 30)
                return $"{(int)(elapsed.TotalDays / 30)} tháng trước";
            if (elapsed.TotalDays >= 1)
                return $"{(int)elapsed.TotalDays} ngày trước";
            if (elapsed.TotalHours >= 1)
                return $"{(int)elapsed.TotalHours} giờ trước";
            return "Vừa xong";
        }
    }
}

/// <summary>
/// Feedback statistics for tutor
/// </summary>
public class FeedbackStatsResponse
{
    public string? TutorId { get; set; }
    public double AverageRating { get; set; }
    public int TotalReviews { get; set; }

    public int Rating5Count { get; set; }
    public int Rating4Count { get; set; }
    public int Rating3Count { get; set; }
    public int Rating2Count { get; set; }
    public int Rating1Count { get; set; }

    // Legacy compatibility
    public int FiveStarCount => Rating5Count;
    public int FourStarCount => Rating4Count;
    public int ThreeStarCount => Rating3Count;
    public int TwoStarCount => Rating2Count;
    public int OneStarCount => Rating1Count;

    /// <summary>
    /// Percentage breakdown
    /// </summary>
    public double Rating5Percent { get; set; }
    public double Rating4Percent { get; set; }
    public double Rating3Percent { get; set; }
    public double Rating2Percent { get; set; }
    public double Rating1Percent { get; set; }

    public decimal FiveStarPercent => TotalReviews > 0 ? (decimal)Rating5Count / TotalReviews * 100 : 0;
    public decimal FourStarPercent => TotalReviews > 0 ? (decimal)Rating4Count / TotalReviews * 100 : 0;
    public decimal ThreeStarPercent => TotalReviews > 0 ? (decimal)Rating3Count / TotalReviews * 100 : 0;
    public decimal TwoStarPercent => TotalReviews > 0 ? (decimal)Rating2Count / TotalReviews * 100 : 0;
    public decimal OneStarPercent => TotalReviews > 0 ? (decimal)Rating1Count / TotalReviews * 100 : 0;
}
