namespace MV.DomainLayer.DTO.ResponseModel.Admin;

/// <summary>
/// Top-level response for GET /api/admin/dashboard/stats
/// </summary>
public class AdminDashboardStatsResponse
{
    public DashboardPlatformOverview PlatformOverview { get; set; } = new();
    public DashboardBookingSummary BookingSummary { get; set; } = new();
    public DashboardLessonSummary LessonSummary { get; set; } = new();
    public DashboardPendingActions PendingActions { get; set; } = new();
}

public class DashboardPlatformOverview
{
    public int TotalUsers { get; set; }
    public int TotalActiveTutors { get; set; }
    public int TotalParents { get; set; }
    public int TotalStudents { get; set; }
    public int PendingTutorApprovals { get; set; }
    public int SuspendedUsers { get; set; }
}

public class DashboardBookingSummary
{
    public int ActiveBookings { get; set; }
    public int CompletedBookings { get; set; }
    public int CancelledBookings { get; set; }
    public decimal GmvThisMonth { get; set; }
    public decimal PlatformRevenueThisMonth { get; set; }
}

public class DashboardLessonSummary
{
    public int LessonsToday { get; set; }
    public decimal? CompletionRatePercent { get; set; }
    public decimal? NoShowRatePercent { get; set; }
}

public class DashboardPendingActions
{
    public int PendingWithdrawals { get; set; }
    public decimal PendingWithdrawalAmount { get; set; }
    public int OpenDisputes { get; set; }
    public int PendingWarnings { get; set; }

    /// <summary>Tutor certificates awaiting admin review (verificationstatus = pending_review).</summary>
    public int PendingCertificates { get; set; }
}
