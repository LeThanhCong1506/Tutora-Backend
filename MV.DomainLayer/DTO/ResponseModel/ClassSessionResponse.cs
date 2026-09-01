namespace MV.DomainLayer.DTO.ResponseModel;

/// <summary>
/// Standard classSession response — used for both list views and basic single-classSession queries.
/// Callers: <c>GetTutorClassSessionsAsync</c>, <c>GetParentClassSessionsAsync</c>, <c>GetClassSessionByIdAsync</c>.
/// For richer tutor/parent actions (check-in, report, no-show), use <see cref="ClassSessionDetailResponse"/>.
/// For student perspective, use <see cref="StudentClassSessionSummaryResponse"/>.
/// </summary>
public class ClassSessionResponse
{
    public int ClassSessionId { get; set; }
    public int BookingId { get; set; }
    public string? TutorId { get; set; }
    public string? StudentId { get; set; }
    public DateTime ScheduledStart { get; set; }
    public DateTime ScheduledEnd { get; set; }
    public string? MeetingLink { get; set; }
    public decimal? ClassSessionPrice { get; set; }
    public string Status { get; set; }
    /// <summary>
    /// Trạng thái của BOOKING chứa buổi này (xem <c>BookingStatus</c>) — khác <see cref="Status"/>
    /// là trạng thái của riêng buổi. FE cần cả hai: một khoá học chỉ thực sự "hoàn thành" khi
    /// booking ở trạng thái completed, không phải khi tình cờ hết buổi đã mở (buổi 2..N có thể
    /// đang ở `reserved` chờ trả nốt tiền — xem ActivateRemainingSessionsAsync).
    /// </summary>
    public string? BookingStatus { get; set; }
    public DateTime? CheckInTime { get; set; }
    public DateTime? CheckOutTime { get; set; }
    public bool? IsTutorPresent { get; set; }
    public bool? IsStudentPresent { get; set; }
    public DateTime? CreatedAt { get; set; }
    /// <summary>True nếu đã có video buổi học xem lại được (không phân biệt đang xử lý/đang ghi).</summary>
    public bool HasRecording { get; set; }
    /// <summary>
    /// Trạng thái yêu cầu đổi lịch (dời lịch) đang còn hiệu lực cho buổi này — "pending" (chờ xác
    /// nhận) hoặc "approved" (đã xác nhận, sẽ áp dụng khi check-in). Null nếu không có yêu cầu nào
    /// đang hiệu lực. Dùng để hiện badge riêng trên danh sách buổi học, KHÔNG liên quan tới Status.
    /// </summary>
    public string? ScheduleChangeStatus { get; set; }
    /// <summary>True nếu buổi này đang có đề xuất đổi lịch (tính năng chủ động chọn giờ mới) chờ phản hồi.</summary>
    public bool HasPendingReschedule { get; set; }
    /// <summary>True nếu đây là buổi phụ (Link 2), sinh ra khi buổi gốc (<see cref="OriginalClassSessionId"/>) bị báo ngắt giữa chừng.</summary>
    public bool? IsContinuation { get; set; }
    /// <summary>True nếu đây là buổi học lại (Link 3), sinh ra khi hoà giải dispute chọn "học lại".</summary>
    public bool? IsDisputeRelearn { get; set; }
    /// <summary>Buổi gốc mà buổi bù/buổi phụ/buổi học lại này trỏ về — null nếu đây là buổi gốc.</summary>
    public int? OriginalClassSessionId { get; set; }
    public StudentMiniResponse? Student { get; set; }
    public SubjectResponse? Subject { get; set; }
    public TutorMiniResponse? Tutor { get; set; }
}
