using MV.DomainLayer.Constants;

namespace MV.DomainLayer.DTO.ResponseModel;

/// <summary>
/// Response for calendar view - classSessions grouped by date
/// </summary>
public class CalendarDayResponse
{
    /// <summary>
    /// Date of the calendar day
    /// </summary>
    public DateTime Date { get; set; }

    /// <summary>
    /// List of classSessions on this day
    /// </summary>
    public List<CalendarClassSessionResponse> ClassSessions { get; set; } = new();
}

/// <summary>
/// ClassSession summary for calendar view
/// </summary>
public class CalendarClassSessionResponse
{
    public int ClassSessionId { get; set; }

    /// <summary>
    /// Booking chứa buổi học — FE tutor dùng để điều hướng tới trang chi tiết lớp
    /// (/tutor-portal/classes/:bookingId) khi bấm vào một buổi trên lịch.
    /// </summary>
    public int? BookingId { get; set; }

    public DateTime ScheduledStart { get; set; }
    public DateTime ScheduledEnd { get; set; }
    public string? StudentName { get; set; }
    public string? TutorName { get; set; }
    public string? SubjectName { get; set; }
    public string? Status { get; set; }
    public string? BookingStatus { get; set; }
    public string? MeetingLink { get; set; }

    /// <summary>
    /// Giờ check-out của buổi học (nếu đã kết thúc). Buổi in_progress mà ĐÃ có CheckOutTime
    /// nghĩa là phòng học đã đóng vĩnh viễn, chỉ còn chờ gia sư gửi báo cáo — FE dựa vào đây
    /// để ẩn nút "Vào lớp" và hiển thị "Chờ gửi báo cáo" thay vì "Đang diễn ra".
    /// </summary>
    public DateTime? CheckOutTime { get; set; }

    /// <summary>True nếu buổi học đã có video xem lại (đã upload xong lên Drive).</summary>
    public bool HasRecording { get; set; }

    /// <summary>
    /// Trạng thái yêu cầu đổi lịch (dời lịch) đang còn hiệu lực — "pending" hoặc "approved". Null
    /// nếu không có yêu cầu nào đang hiệu lực. Chỉ được populate ở lịch của student hiện tại.
    /// </summary>
    public string? ScheduleChangeStatus { get; set; }

    /// <summary>True nếu buổi này đang có đề xuất đổi lịch (tính năng chủ động chọn giờ mới) chờ phản hồi.</summary>
    public bool HasPendingReschedule { get; set; }

    /// <summary>True nếu đây là buổi phụ (Link 2), sinh ra khi buổi gốc (<see cref="OriginalClassSessionId"/>) bị báo ngắt giữa chừng.</summary>
    public bool? IsContinuation { get; set; }
    /// <summary>True nếu đây là buổi học lại (Link 3), sinh ra khi hoà giải dispute chọn "học lại".</summary>
    public bool? IsDisputeRelearn { get; set; }
    /// <summary>Buổi gốc mà buổi phụ/buổi học lại này trỏ về — null nếu đây là buổi gốc.</summary>
    public int? OriginalClassSessionId { get; set; }

    /// <summary>
    /// Color code based on status for UI rendering
    /// </summary>
    public string StatusColor => Status switch
    {
        ClassSessionStatus.Scheduled => "#3B82F6",           // Blue
        ClassSessionStatus.InProgress => "#22C55E",         // Green
        ClassSessionStatus.PendingConfirmation => "#F59E0B", // Amber
        ClassSessionStatus.Completed => "#10B981",           // Emerald
        ClassSessionStatus.Cancelled => "#6B7280",           // Gray
        ClassSessionStatus.Disputed => "#EF4444",            // Red
        ClassSessionStatus.NoShow => "#DC2626",             // Dark Red
        ClassSessionStatus.Interrupted => "#F59E0B",         // Amber — khớp tông trang chi tiết
        _ => "#9CA3AF"                      // Default Gray
    };
}
