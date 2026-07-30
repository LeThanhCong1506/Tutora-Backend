namespace MV.DomainLayer.DTO.ResponseModel.Admin;

/// <summary>Số liệu cho bộ báo cáo doanh thu (/admin-portal/revenue-reports).</summary>
public class AdminRevenueOverviewResponse
{
    public RevenueSummaryDto Summary { get; set; } = new();
    public List<RevenueTrendPointDto> Trend { get; set; } = [];
    public List<NamedValueDto> RevenueMix { get; set; } = [];
    public List<FunnelStepDto> BookingFunnel { get; set; } = [];
}

public class RevenueSummaryDto
{
    /// <summary>Phí của buổi đã dạy xong trong kỳ + doanh thu AI trong kỳ.</summary>
    public decimal RecognisedRevenue { get; set; }
    public decimal RecognisedPrevious { get; set; }

    /// <summary>Toàn bộ phí của booking tạo trong kỳ + doanh thu AI (cách cũ).</summary>
    public decimal ContractedRevenue { get; set; }
    public decimal ContractedPrevious { get; set; }

    /// <summary>Đã thu tiền nhưng buổi học chưa dạy — nghĩa vụ dịch vụ phải giao.</summary>
    public decimal DeferredRevenue { get; set; }
    public decimal DeferredPrevious { get; set; }

    /// <summary>Tổng giá trị giao dịch — tiền phụ huynh trả, KHÔNG phải doanh thu.</summary>
    public decimal Gmv { get; set; }
    public decimal GmvPrevious { get; set; }

    /// <summary>Tiền mặt thực thu trong kỳ (payment_transactions thành công).</summary>
    public decimal CashCollected { get; set; }
    public decimal CashPrevious { get; set; }
}

public class RevenueTrendPointDto
{
    public string Month { get; set; } = "";
    public decimal Recognised { get; set; }
    public decimal Contracted { get; set; }
    public decimal AiRevenue { get; set; }
    public decimal Gmv { get; set; }
}

public class NamedValueDto
{
    public string Name { get; set; } = "";
    public decimal Value { get; set; }
}

public class FunnelStepDto
{
    public string Stage { get; set; } = "";
    public string Label { get; set; } = "";
    public int Count { get; set; }
}

/// <summary>Hoàn tiền trong kỳ. Nguồn: wallet_transactions type=Refund.</summary>
public class RefundStatsDto
{
    public decimal Amount { get; set; }
    public decimal AmountPrevious { get; set; }
    public int Count { get; set; }
    public int CountPrevious { get; set; }

    /// <summary>% trên tiền mặt đã thu trong kỳ.</summary>
    public decimal RateOfCash { get; set; }
}

public class RefundTrendPointDto
{
    public string Month { get; set; } = "";
    public decimal Amount { get; set; }
    public int Count { get; set; }
}

/// <summary>Trả đợt 1 nhưng chưa học buổi nào — khác <see cref="StalledBookingStatsDto"/>.</summary>
public class NeverStartedStatsDto
{
    public int Count { get; set; }
    public int CountPrevious { get; set; }

    /// <summary>Hoa hồng của số buổi đã bán.</summary>
    public decimal FeeAtRisk { get; set; }

    /// <summary>Tiền khách đã trả (GMV).</summary>
    public decimal CashHeld { get; set; }
}

public class AdminRevenueRecognitionResponse
{
    public RevenueSummaryDto Summary { get; set; } = new();
    public List<DeferredAgingDto> DeferredAging { get; set; } = [];
    public StalledBookingStatsDto Stalled { get; set; } = new();
    public NeverStartedStatsDto NeverStarted { get; set; } = new();
    public List<StalledTrendPointDto> StalledTrend { get; set; } = [];
    public RefundStatsDto Refunds { get; set; } = new();
    public List<RefundTrendPointDto> RefundTrend { get; set; } = [];
    public List<BookingProgressDto> BookingProgress { get; set; } = [];
}

public class DeferredAgingDto
{
    public string Bucket { get; set; } = "";
    public decimal Amount { get; set; }
    public int Bookings { get; set; }
}

/// <summary>Booking dừng ở deposit_paid quá hạn trả đợt 2 — rò rỉ doanh thu.</summary>
public class StalledBookingStatsDto
{
    public int Count { get; set; }
    public int CountPrevious { get; set; }
    public decimal ContractedFeeAtRisk { get; set; }
    public decimal DropOffRate { get; set; }
    public decimal DropOffPrevious { get; set; }
}

public class StalledTrendPointDto
{
    public string Month { get; set; } = "";
    public int Stalled { get; set; }
    public int Converted { get; set; }
}

public class BookingProgressDto
{
    public int BookingId { get; set; }
    public string ParentName { get; set; } = "";
    public string TutorName { get; set; } = "";
    public string Subject { get; set; } = "";
    public int TotalSessions { get; set; }
    public int DeliveredSessions { get; set; }
    public decimal ContractedFee { get; set; }
    public decimal RecognisedFee { get; set; }
    public DateTime? CreatedAt { get; set; }
    public string Status { get; set; } = "";
}

public class AdminTutorRevenueResponse
{
    /// <summary>Đã cắt còn <c>top</c> dòng — đừng dùng Count làm số liệu.</summary>
    public List<TutorRevenueDto> Tutors { get; set; } = [];

    /// <summary>Số gia sư dạy xong ít nhất một buổi trong kỳ.</summary>
    public int TutorsWithRevenue { get; set; }

    /// <summary>Số gia sư có buổi trong kỳ, kể cả huỷ hết.</summary>
    public int ActiveTutors { get; set; }

    public List<NamedValueDto> Concentration { get; set; } = [];
    public decimal TotalPlatformRevenue { get; set; }

    /// <summary>Escrow toàn sàn hiện tại — nợ phải trả, KHÔNG lọc theo kỳ.</summary>
    public decimal TotalEscrowHeld { get; set; }
}

public class TutorRevenueDto
{
    public string TutorId { get; set; } = "";
    public string TutorName { get; set; } = "";
    public string Subject { get; set; } = "";
    public decimal Gmv { get; set; }
    public decimal PlatformRevenue { get; set; }

    /// <summary>% giữ lại trên GMV. Chỉ so sánh tương đối — hai vế khác mốc thời gian.</summary>
    public decimal TakeRate { get; set; }

    public decimal TutorEarnings { get; set; }
    public decimal EscrowHeld { get; set; }
    public int SessionsDelivered { get; set; }
    public decimal RevenuePerSession { get; set; }
    public decimal CancelRate { get; set; }
    public int DisputeCount { get; set; }
    public decimal Rating { get; set; }
}

public class AdminCustomerRevenueResponse
{
    public CustomerSummaryDto Summary { get; set; } = new();

    /// <summary>Phân khúc người chi tiền: phụ huynh vs học sinh tự đặt.</summary>
    public List<CustomerSegmentDto> Segments { get; set; } = [];

    public List<ParentRevenueDto> Parents { get; set; } = [];
    public List<ArpuPointDto> ArpuTrend { get; set; } = [];
    public List<NewVsReturningDto> NewVsReturning { get; set; } = [];
    public List<NamedCountDto> BookingValueDistribution { get; set; } = [];
    public List<CohortRowDto> Cohorts { get; set; } = [];
}

/// <summary>Phân khúc khách. Số liệu trong kỳ, trừ Ltv/RepeatRate tính toàn lịch sử.</summary>
public class CustomerSegmentDto
{
    public string Segment { get; set; } = "";

    /// <summary>Số khách khác nhau có booking trong kỳ.</summary>
    public int Customers { get; set; }

    public int Bookings { get; set; }

    /// <summary>Tiền khách trả (GMV), không phải hoa hồng.</summary>
    public decimal TotalSpent { get; set; }

    /// <summary>Hoa hồng nền tảng thực nhận từ nhóm này — buổi đã dạy.</summary>
    public decimal PlatformRevenue { get; set; }

    /// <summary>Chi tiêu bình quân mỗi khách, toàn lịch sử.</summary>
    public decimal Ltv { get; set; }

    public decimal AvgBookingValue { get; set; }

    /// <summary>% khách đặt từ 2 booking trở lên.</summary>
    public decimal RepeatRate { get; set; }
}

public class CustomerSummaryDto
{
    public int ActiveParents { get; set; }
    public decimal RepeatRate { get; set; }
    public decimal RepeatRatePrevious { get; set; }
    public decimal AvgBookingValue { get; set; }
    public decimal AvgBookingValuePrevious { get; set; }
    public decimal Ltv { get; set; }
}

public class ParentRevenueDto
{
    /// <summary>Phụ huynh, hoặc học sinh nếu tự đặt lịch.</summary>
    public string ParentId { get; set; } = "";
    public string ParentName { get; set; } = "";

    /// <summary>"Phụ huynh" hoặc "Học sinh".</summary>
    public string CustomerType { get; set; } = "";

    public string StudentName { get; set; } = "";
    public decimal TotalSpent { get; set; }
    public int BookingCount { get; set; }
    public int SessionsPurchased { get; set; }
    public int SessionsCompleted { get; set; }

    /// <summary>Hoa hồng buổi chưa học. KHÔNG suy ra từ TotalSpent (gồm phần gia sư).</summary>
    public decimal DeferredRevenue { get; set; }
    public DateTime? FirstBookingAt { get; set; }
    public DateTime? LastBookingAt { get; set; }
}

public class ArpuPointDto
{
    public string Month { get; set; } = "";
    public decimal Arpu { get; set; }
    public int ActiveParents { get; set; }
}

public class NewVsReturningDto
{
    public string Month { get; set; } = "";
    public int NewCustomers { get; set; }
    public int Returning { get; set; }
}

public class NamedCountDto
{
    public string Range { get; set; } = "";
    public int Count { get; set; }
}

public class CohortRowDto
{
    public string Cohort { get; set; } = "";
    public int Size { get; set; }
    /// <summary>% còn hoạt động ở tháng thứ 0..N; null = chưa tới kỳ đó.</summary>
    public List<decimal?> Retention { get; set; } = [];
}

public class AdminSubjectRevenueResponse
{
    public List<SubjectRevenueDto> Subjects { get; set; } = [];
    public List<GradeRevenueDto> Grades { get; set; } = [];
    public List<SubjectGradeCellDto> Matrix { get; set; } = [];
    public List<Dictionary<string, object>> SubjectTrend { get; set; } = [];
}

public class SubjectRevenueDto
{
    public int SubjectId { get; set; }
    public string SubjectName { get; set; } = "";
    public decimal Gmv { get; set; }
    public decimal PlatformRevenue { get; set; }

    /// <summary>Hoa hồng buổi chưa dạy. KHÔNG phải Gmv − PlatformRevenue (khác cơ sở).</summary>
    public decimal DeferredRevenue { get; set; }

    public int Bookings { get; set; }
    public int SessionsDelivered { get; set; }
    public decimal AvgPricePerSession { get; set; }
    public decimal CompletionRate { get; set; }
}

public class GradeRevenueDto
{
    public int GradeId { get; set; }
    public string GradeName { get; set; } = "";
    public decimal Gmv { get; set; }
    public decimal PlatformRevenue { get; set; }
    public int Bookings { get; set; }
}

public class SubjectGradeCellDto
{
    public string Subject { get; set; } = "";
    public string Grade { get; set; } = "";
    public decimal Revenue { get; set; }
}

public class AdminAiRevenueResponse
{
    public AiSummaryDto Summary { get; set; } = new();
    public List<AiPackageDto> Packages { get; set; } = [];
    public List<AiCreditFlowDto> CreditFlow { get; set; } = [];
    public List<AiTopUserDto> TopUsers { get; set; } = [];
    public List<RevenueTrendPointDto> Trend { get; set; } = [];
}

public class AiSummaryDto
{
    public decimal Revenue { get; set; }
    public decimal RevenuePrevious { get; set; }
    public int PackagesSold { get; set; }
    public int PackagesSoldPrevious { get; set; }
    /// <summary>Tổng credit đã cấp (tặng Free + mua gói), luỹ kế toàn hệ thống.</summary>
    public int CreditsSold { get; set; }

    /// <summary>Tổng lượt đã hỏi, cộng từ ai_usage_monthly.</summary>
    public int CreditsConsumed { get; set; }

    /// <summary>Credit còn lại — không quy ra tiền được vì mỗi gói một đơn giá.</summary>
    public int CreditsOutstanding { get; set; }

    // Tách độ phủ (TotalUsers/ActivatedUsers) khỏi cường độ (ActivatedCredits*): mọi
    // tài khoản đều được tặng lượt nên chia trên tổng sẽ ra tỷ lệ ~1% vô nghĩa.

    /// <summary>Số tài khoản được cấp lượt AI.</summary>
    public int TotalUsers { get; set; }

    /// <summary>Số tài khoản đã hỏi ít nhất một lượt.</summary>
    public int ActivatedUsers { get; set; }

    /// <summary>Lượt đã cấp cho riêng nhóm đã kích hoạt.</summary>
    public int ActivatedCreditsGranted { get; set; }

    /// <summary>Lượt đã dùng của riêng nhóm đã kích hoạt.</summary>
    public int ActivatedCreditsConsumed { get; set; }
}

public class AiPackageDto
{
    public int PackageId { get; set; }
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    public int CreditAmount { get; set; }
    public int UnitsSold { get; set; }
    public decimal Revenue { get; set; }
}

/// <summary>Lượt AI được cấp và được dùng trong tháng.</summary>
public class AiCreditFlowDto
{
    public string Month { get; set; } = "";

    /// <summary>Lượt cấp trong tháng, chỉ tính tài khoản đã từng hỏi bài.</summary>
    public int Granted { get; set; }

    public int Consumed { get; set; }
}

public class AiTopUserDto
{
    public string UserId { get; set; } = "";
    public string UserName { get; set; } = "";
    public string Role { get; set; } = "";
    public int CreditsConsumed { get; set; }
    public int CreditsPurchased { get; set; }
    public decimal AmountPaid { get; set; }
}
