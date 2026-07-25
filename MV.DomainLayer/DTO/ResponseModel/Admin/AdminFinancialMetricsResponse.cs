namespace MV.DomainLayer.DTO.ResponseModel.Admin;

/// <summary>
/// Top-level response for GET /api/admin/financials/metrics
/// </summary>
public class AdminFinancialMetricsResponse
{
    public RevenueOverviewMetrics Revenue { get; set; } = new();
    public BookingMetrics Bookings { get; set; } = new();
    public ClassSessionMetrics ClassSessions { get; set; } = new();
    public UserGrowthMetrics Users { get; set; } = new();
    public WithdrawalMetrics Withdrawals { get; set; } = new();
    public EscrowMetrics Escrow { get; set; } = new();
    public List<RevenueTrendItem> RevenueTrend { get; set; } = new();
    public List<TopSubjectItem> TopSubjects { get; set; } = new();
    /// <summary>Date range this report was computed for (Vietnam time)</summary>
    public DateTime? FilterFrom { get; set; }
    public DateTime? FilterTo { get; set; }
    public string Period { get; set; } = "month";
}

// ─── Revenue ─────────────────────────────────────────────────────────────────

public class RevenueOverviewMetrics
{
    /// <summary>TỔNG doanh thu nền tảng all-time = platform fee booking + doanh thu bán gói AI credit.</summary>
    public decimal TotalPlatformRevenue { get; set; }
    /// <summary>Total gross transaction value (Booking.Finalprice) all-time — chỉ booking.</summary>
    public decimal TotalGrossVolume { get; set; }
    /// <summary>Tổng doanh thu tháng này = booking platform fee + AI credit tháng này.</summary>
    public decimal CurrentMonthRevenue { get; set; }
    /// <summary>Tổng doanh thu tháng trước.</summary>
    public decimal PreviousMonthRevenue { get; set; }
    /// <summary>Month-over-month growth % (null if previous month = 0)</summary>
    public decimal? MonthOverMonthGrowthPercent { get; set; }
    /// <summary>Tổng doanh thu năm nay.</summary>
    public decimal CurrentYearRevenue { get; set; }
    /// <summary>Total amount currently in escrow (frozen wallet balances)</summary>
    public decimal TotalEscrowed { get; set; }

    /// <summary>Bóc tách doanh thu theo nguồn (booking vs AI credit) để CEO quan sát dòng tiền / lọc.</summary>
    public RevenueBySource BySource { get; set; } = new();
}

/// <summary>Doanh thu tách theo nguồn. Tổng của các nguồn = TotalPlatformRevenue.</summary>
public class RevenueBySource
{
    /// <summary>Doanh thu từ phí nền tảng của booking (all-time).</summary>
    public decimal Booking { get; set; }
    /// <summary>Doanh thu từ bán gói AI credit (Homework Helper), payment_transactions
    /// purpose=AiCreditPurchase, status=Succeeded (all-time).</summary>
    public decimal AiCredit { get; set; }
}

// ─── Bookings ────────────────────────────────────────────────────────────────

public class BookingMetrics
{
    public int Total { get; set; }
    public int Active { get; set; }        // paid + deposit_paid + ongoing + pending_remaining_payment
    public int Completed { get; set; }
    public int Cancelled { get; set; }
    public int PendingTutor { get; set; }
    /// <summary>New bookings created in selected period / current month</summary>
    public int NewThisPeriod { get; set; }
    public List<BookingStatusCount> ByStatus { get; set; } = new();
    public List<BookingTeachingModeCount> ByTeachingMode { get; set; } = new();
}

public class BookingStatusCount
{
    public string Status { get; set; } = string.Empty;
    public int Count { get; set; }
}

public class BookingTeachingModeCount
{
    public string Mode { get; set; } = string.Empty;
    public int Count { get; set; }
}

// ─── ClassSessions ─────────────────────────────────────────────────────────────────

public class ClassSessionMetrics
{
    public int TotalCompleted { get; set; }
    public int TotalScheduled { get; set; }
    public int TotalNoShow { get; set; }
    public int TotalCancelled { get; set; }
    public int TotalDisputed { get; set; }
    /// <summary>Completion rate = Completed / (Completed + Cancelled + NoShow)</summary>
    public decimal? CompletionRatePercent { get; set; }
    /// <summary>No-show rate %</summary>
    public decimal? NoShowRatePercent { get; set; }
    /// <summary>Total classSession revenue (sum of ClassSession.Lessonprice for settled classSessions)</summary>
    public decimal TotalClassSessionRevenue { get; set; }
}

// ─── Users ───────────────────────────────────────────────────────────────────

public class UserGrowthMetrics
{
    public int TotalTutors { get; set; }
    public int TotalParents { get; set; }
    public int TotalStudents { get; set; }
    public int ActiveTutors { get; set; }   // tutorprofile.Profilestatus = 'active' && Ispublic = true
    public int NewTutorsThisMonth { get; set; }
    public int NewParentsThisMonth { get; set; }
    /// <summary>Average tutor rating across all active tutors</summary>
    public decimal? AverageTutorRating { get; set; }
}

// ─── Withdrawals ─────────────────────────────────────────────────────────────

public class WithdrawalMetrics
{
    public int TotalPending { get; set; }
    public decimal TotalPendingAmount { get; set; }
    public int TotalApproved { get; set; }
    public decimal TotalApprovedAmount { get; set; }
    public int TotalRejected { get; set; }
    public decimal TotalRejectedAmount { get; set; }
    public int TotalCancelled { get; set; }
    public decimal TotalCancelledAmount { get; set; }
    /// <summary>Withdrawals processed this month</summary>
    public int ProcessedThisMonth { get; set; }
    public decimal ProcessedAmountThisMonth { get; set; }
}

// ─── Escrow ──────────────────────────────────────────────────────────────────

public class EscrowMetrics
{
    /// <summary>Sum of all wallet Frozenbalance (money waiting for settlement)</summary>
    public decimal TotalFrozenBalance { get; set; }
    /// <summary>Total EscrowRelease transactions (paid out to tutors) all-time</summary>
    public decimal TotalReleasedToTutors { get; set; }
    /// <summary>Total Refund transactions (returned to parents) all-time</summary>
    public decimal TotalRefundedToParents { get; set; }
}

// ─── Trend ───────────────────────────────────────────────────────────────────

public class RevenueTrendItem
{
    /// <summary>
    /// Label for the period:
    ///   period=month → "2025-01"
    ///   period=week  → "2025-W01"
    ///   period=year  → "2025"
    /// </summary>
    public string Label { get; set; } = string.Empty;
    public decimal PlatformRevenue { get; set; }
    public decimal GrossVolume { get; set; }
    public int BookingCount { get; set; }
    public int ClassSessionsCompleted { get; set; }
}

// ─── Top Subjects ─────────────────────────────────────────────────────────────

public class TopSubjectItem
{
    public int SubjectId { get; set; }
    public string SubjectName { get; set; } = string.Empty;
    public int BookingCount { get; set; }
    public decimal TotalRevenue { get; set; }
}
