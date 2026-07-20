namespace MV.DomainLayer.DTO.ResponseModel;

public static class UserPresenceStatus
{
    public const string Online = "online";
    public const string Offline = "offline";
    public const string Unknown = "unknown";
}

/// <summary>
/// Trạng thái hoạt động của một người dùng dùng cho hiển thị "đang online / offline bao lâu".
/// </summary>
public class UserPresenceResponse
{
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// True/false khi Redis khả dụng; null nghĩa là presence tạm thời không xác định.
    /// Caller không được diễn giải null thành offline.
    /// </summary>
    public bool? IsOnline { get; set; }

    /// <summary><c>online</c>, <c>offline</c> hoặc <c>unknown</c>.</summary>
    public string Status { get; set; } = UserPresenceStatus.Unknown;

    /// <summary>
    /// Phiên bản tăng đơn điệu theo mỗi transition. Client dùng để bỏ qua event
    /// realtime đến trễ sau reconnect.
    /// </summary>
    public long Version { get; set; }

    /// <summary>
    /// Redis state epoch. Nếu Redis bị flush/recreated, epoch đổi và client phải
    /// bỏ version cũ thay vì so sánh hai bộ đếm thuộc hai epoch khác nhau.
    /// </summary>
    public string? Epoch { get; set; }

    /// <summary>
    /// Thời điểm cuối cùng người dùng còn online (UTC). Chỉ có ý nghĩa khi <see cref="IsOnline"/> = false.
    /// Null nếu chưa từng ghi nhận.
    /// </summary>
    public DateTime? LastSeenAt { get; set; }
}
