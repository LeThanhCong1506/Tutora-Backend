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

    /// <summary>True nếu gia sư đã accept 1 booking khác trùng khung giờ này (deposit_paid trở
    /// lên) — chỉ trường hợp này mới thực sự khóa khung giờ. Booking đang pending_tutor/
    /// pending_payment KHÔNG khóa, chỉ được tính vào PendingCount bên dưới.</summary>
    public bool IsLocked { get; set; }

    /// <summary>Số booking khác nhau đang pending_tutor (đã đóng cọc, chờ gia sư xác nhận) trùng
    /// khung giờ này. FE dùng để cảnh báo "đang có N người muốn chọn khung giờ này" khi >= 2.</summary>
    public int PendingCount { get; set; }
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
    public string? Description { get; set; }
    public bool IsActive { get; set; }
    public string? Slug { get; set; }
    public string? IconUrl { get; set; }
    public bool IsHomeworkEnabled { get; set; }
    public int DisplayOrder { get; set; }
    /// <summary>Khối lớp thấp nhất môn này áp dụng. Null = không giới hạn.</summary>
    public int? MinGradeLevelId { get; set; }
    /// <summary>Khối lớp cao nhất môn này áp dụng. Null = không giới hạn.</summary>
    public int? MaxGradeLevelId { get; set; }
}

public class GradeLevelResponse
{
    public int GradeLevelId { get; set; }
    public string? GradeName { get; set; }
    public int LevelOrder { get; set; }
    public bool IsActive { get; set; }
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

    /// <summary>True nếu đây là buổi phụ (Link 2), sinh ra khi buổi gốc (<see cref="OriginalClassSessionId"/>) bị báo ngắt giữa chừng.</summary>
    public bool IsContinuation { get; set; }
    /// <summary>True nếu đây là buổi học lại (Link 3), sinh ra khi hoà giải dispute chọn "học lại".</summary>
    public bool IsDisputeRelearn { get; set; }
    /// <summary>Buổi gốc mà buổi phụ/buổi học lại này trỏ về — null nếu đây là buổi gốc.</summary>
    public int? OriginalClassSessionId { get; set; }
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

    /// <summary>
    /// Vai trò của người TẠO booking ("Student" / "Parent"). Booking do học sinh tạo dừng ở
    /// pending_payment chờ phụ huynh duyệt và trả tiền — FE dùng cờ này để gắn nhãn phân biệt
    /// trong cùng danh sách đặt lịch của phụ huynh.
    /// </summary>
    public string? CreatedByRole { get; set; }
    /// <summary>UTC deadline for the tutor to accept or decline a pending booking.</summary>
    public DateTime? ResponseDeadline { get; set; }
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
