namespace MV.DomainLayer.DTO.ResponseModel;

/// <summary>
/// Dashboard statistics for tutor
/// </summary>
public class TutorDashboardStatsResponse
{
    /// <summary>Number of upcoming lessons (scheduled)</summary>
    public int UpcomingLessons { get; set; }

    /// <summary>Number of lessons completed this month</summary>
    public int CompletedThisMonth { get; set; }

    /// <summary>Total lessons completed</summary>
    public int TotalCompleted { get; set; }

    /// <summary>Earnings this month (VND)</summary>
    public decimal EarningsThisMonth { get; set; }

    /// <summary>Total earnings (VND)</summary>
    public decimal TotalEarnings { get; set; }

    /// <summary>Current wallet balance (VND)</summary>
    public decimal WalletBalance { get; set; }

    /// <summary>Frozen balance in escrow (VND)</summary>
    public decimal FrozenBalance { get; set; }

    /// <summary>Pending confirmation lessons</summary>
    public int PendingConfirmation { get; set; }

    /// <summary>Active disputes</summary>
    public int ActiveDisputes { get; set; }

    /// <summary>Average rating (1-5)</summary>
    public double AverageRating { get; set; }

    /// <summary>Total reviews count</summary>
    public int TotalReviews { get; set; }

    /// <summary>List of next 5 upcoming lessons</summary>
    public List<UpcomingLessonResponse> NextLessons { get; set; } = new();

    /// <summary>Profile status: draft, pending_approval, active, rejected</summary>
    public string? ProfileStatus { get; set; }

    /// <summary>
    /// Whether tutor has at least 1 verified certificate (admin approved).
    /// Used by FE to show specific banner when draft + certificates verified.
    /// </summary>
    public bool HasVerifiedCertificates { get; set; }

    /// <summary>
    /// List of missing required field keys when profile is draft.
    /// Keys: headline, teachingArea, teachingMode, subjects, bio, education, hourlyRate, avatar, video
    /// </summary>
    public List<string>? MissingFields { get; set; }
}

/// <summary>
/// Upcoming lesson summary
/// </summary>
public class UpcomingLessonResponse
{
    public int LessonId { get; set; }
    public int? BookingId { get; set; }
    public DateTime ScheduledStart { get; set; }
    public DateTime ScheduledEnd { get; set; }
    public string? StudentName { get; set; }
    public string? SubjectName { get; set; }
    public string? MeetingLink { get; set; }
}
