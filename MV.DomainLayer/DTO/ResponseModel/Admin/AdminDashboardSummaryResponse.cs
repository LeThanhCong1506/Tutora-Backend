namespace MV.DomainLayer.DTO.ResponseModel.Admin;

public class AdminDashboardSummaryResponse
{
    public MetricWithChange Gmv { get; set; } = new();
    public MetricWithChange PlatformRevenue { get; set; } = new();
    public SummaryBookings Bookings { get; set; } = new();
    public SummaryPendingActions PendingActions { get; set; } = new();

    /// <summary>UTC start of the period used for GMV/revenue calculations.</summary>
    public DateTime FilterFrom { get; set; }

    /// <summary>UTC end of the period used for GMV/revenue calculations.</summary>
    public DateTime FilterTo { get; set; }
}

/// <summary>A numeric KPI value together with its percent-change vs. the previous equal-length period.</summary>
public class MetricWithChange
{
    public decimal Value { get; set; }

    /// <summary>null when the previous period had zero value (division by zero guard).</summary>
    public decimal? ChangePercent { get; set; }
}

public class SummaryBookings
{
    /// <summary>Bookings currently in an active status (Paid / DepositPaid / PendingRemainingPayment / Ongoing) — snapshot, no period filter.</summary>
    public int Active { get; set; }

    /// <summary>Bookings created within the selected period.</summary>
    public int NewInPeriod { get; set; }

    /// <summary>Bookings with status=Completed whose creation date falls within the period.</summary>
    public int CompletedInPeriod { get; set; }
}

public class SummaryPendingActions
{
    /// <summary>Sum of all sub-counts below — the total admin to-do list.</summary>
    public int Total { get; set; }

    /// <summary>Tutor profiles awaiting approval (status = PendingApproval).</summary>
    public int TutorApprovals { get; set; }

    /// <summary>Tutor certificates awaiting admin review (verificationstatus = pending_review).</summary>
    public int PendingCertificates { get; set; }

    /// <summary>Withdrawal requests awaiting admin review (status = Pending or PendingReview).</summary>
    public int WithdrawalReviews { get; set; }

    /// <summary>Disputes currently open (status = Pending or Investigating).</summary>
    public int OpenDisputes { get; set; }

    /// <summary>System alerts that have not yet been resolved (Systemalerts.Resolved = false).</summary>
    public int UnresolvedAlerts { get; set; }

    /// <summary>Withdrawal requests explicitly marked Delayed — items past their expected processing SLA.</summary>
    public int OverdueCount { get; set; }
}
