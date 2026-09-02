namespace MV.DomainLayer.DTO.ResponseModel.Admin;

public class AdminDashboardSummaryResponse
{
    /// <summary>
    /// Tiền phụ huynh trả (GMV) của các booking tạo trong kỳ.
    ///
    /// Lấy thẳng từ <c>AdminRevenueAnalyticsService.GetOverviewAsync</c> nên khớp TUYỆT ĐỐI
    /// với trang Báo cáo doanh thu. Trước đây dashboard tự tính bằng bộ lọc status riêng, vốn
    /// loại sạch mọi booking đã huỷ — kể cả khoá phụ huynh đã trả tiền và đã học vài buổi —
    /// nên hai trang báo hai con số khác nhau cho cùng một khoảng thời gian.
    /// </summary>
    public MetricWithChange Gmv { get; set; } = new();

    /// <summary>
    /// Học phí gốc — giá gia sư niêm yết, tức <see cref="Gmv"/> TRỪ phần phí phụ huynh cộng thêm.
    ///
    /// Cần lộ ra vì phí sàn 10% tính trên số NÀY, không phải trên Gmv. Thiếu nó thì người đọc
    /// lấy doanh thu tạm tính chia Gmv ra 9,5% và tưởng hệ thống tính sai — đúng câu hỏi đã
    /// phát sinh thật 02/09/2026. Trang Báo cáo doanh thu vốn đã hiện dòng này; dashboard thì
    /// chưa, nên trên dashboard không có cách nào tự nối hai con số lại.
    ///
    /// Không kèm % thay đổi: đây là mẫu số để đọc con số khác, không phải chỉ tiêu tự thân.
    /// Tương ứng <c>RevenueSummaryDto.BaseAmount</c>.
    /// </summary>
    public decimal BaseAmount { get; set; }

    /// <summary>
    /// Doanh thu TẠM TÍNH: phí nền tảng chốt tại thời điểm đặt lịch của các booking tạo trong
    /// kỳ. Chưa phải tiền thật — buổi học chưa dạy thì khoản này vẫn có thể mất.
    /// Tương ứng <c>RevenueSummaryDto.CommissionSold</c>.
    /// </summary>
    public MetricWithChange PlatformRevenue { get; set; } = new();

    /// <summary>
    /// Doanh thu ĐÃ GHI NHẬN: phí nền tảng của các buổi đã dạy xong và đã giải ngân trong kỳ,
    /// cộng phần chốt thêm khi đóng sổ và tiền bán gói AI. Đây mới là doanh thu thật của hệ
    /// thống. Tương ứng <c>RevenueSummaryDto.RecognisedRevenue</c>.
    /// </summary>
    public MetricWithChange RecognisedRevenue { get; set; } = new();

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
