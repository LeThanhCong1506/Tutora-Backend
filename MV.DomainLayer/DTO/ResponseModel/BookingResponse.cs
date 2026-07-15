namespace MV.DomainLayer.DTO.ResponseModel;

public class ScheduleItemResponse
{
    public int DayOfWeek { get; set; }
    public string StartTime { get; set; } = null!;
    public string EndTime { get; set; } = null!;
}

public class BookedSlotResponse
{
    public DateTime ScheduledStart { get; set; }
    public DateTime ScheduledEnd { get; set; }
}

public class StudentMiniResponse
{
    public string StudentId { get; set; } = null!;
    public string? FullName { get; set; }
    public int? GradeLevelId { get; set; }
    public string? GradeLevel { get; set; }
    public string? GradeLevelName { get; set; }
}

public class TutorMiniResponse
{
    public string TutorId { get; set; } = null!;
    public string? FullName { get; set; }
    public string? AvatarUrl { get; set; }
    public decimal? HourlyRate { get; set; }
}

public class SubjectResponse
{
    public int SubjectId { get; set; }
    public string? SubjectName { get; set; }
}

public class GradeLevelResponse
{
    public int GradeLevelId { get; set; }
    public string? GradeName { get; set; }
    public int LevelOrder { get; set; }
}

public class BookingPackageResponse
{
    public int PackageId { get; set; }
    public string? Name { get; set; }
    public int PackageType { get; set; }
}

public class BookingClassSessionSlotResponse
{
    public int ClassSessionId { get; set; }
    public int SessionIndex { get; set; }
    public DateTime ScheduledStart { get; set; }
    public DateTime ScheduledEnd { get; set; }
    public string? Status { get; set; }
    public decimal? ClassSessionPrice { get; set; }
}

public class BookingResponse
{
    public int BookingId { get; set; }
    public string? ParentId { get; set; }
    public StudentMiniResponse? Student { get; set; }
    public TutorMiniResponse? Tutor { get; set; }
    public SubjectResponse? Subject { get; set; }
    public GradeLevelResponse? GradeLevel { get; set; }
    public BookingPackageResponse? Package { get; set; }
    public int? TutorSubjectGradePriceId { get; set; }
    public int? PackageType { get; set; }
    public int? SessionCount { get; set; }
    public int? TotalSessions { get; set; }
    public int? DurationMinutesPerSession { get; set; }
    public decimal? Price { get; set; }
    public decimal? PricePerHour { get; set; }
    public decimal? TotalAmount { get; set; }
    public string? Currency { get; set; }
    public decimal? DiscountApplied { get; set; }
    /// <summary>Học phí sau khuyến mãi, trước phí dịch vụ.</summary>
    public decimal? BaseAmount { get; set; }
    /// <summary>Phí dịch vụ thu từ phụ huynh.</summary>
    public decimal? ParentFee { get; set; }
    /// <summary>Phí dịch vụ khấu trừ từ gia sư.</summary>
    public decimal? TutorServiceFee { get; set; }
    /// <summary>Số tiền gia sư thực nhận cho toàn bộ booking.</summary>
    public decimal? TutorReceivable { get; set; }
    /// <summary>Tổng tiền phụ huynh cần thanh toán, gồm học phí và phí phía phụ huynh.</summary>
    public decimal? FinalPrice { get; set; }
    /// <summary>Tổng phí nền tảng thu từ cả phụ huynh và gia sư.</summary>
    public decimal? PlatformFee { get; set; }
    public string? Status { get; set; }
    public string? PaymentStatus { get; set; }
    public string? PaymentCode { get; set; }
    public List<ScheduleItemResponse>? Schedule { get; set; }
    public List<BookingClassSessionSlotResponse>? ClassSessions { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? PaymentDueAt { get; set; }
    public decimal? DepositAmount { get; set; }
    public decimal? RemainingAmount { get; set; }
    public DateTime? DepositPaidAt { get; set; }
    public DateTime? RemainingPaidAt { get; set; }
    public string? EscrowStatus { get; set; }
    public decimal? RefundAmount { get; set; }
    public string? RefundStatus { get; set; }
    public string? CancellationReason { get; set; }
    public string? CancelledBy { get; set; }
    public DateTime? CancelledAt { get; set; }
}
