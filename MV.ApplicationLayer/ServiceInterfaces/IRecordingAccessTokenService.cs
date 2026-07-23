namespace MV.ApplicationLayer.ServiceInterfaces;

/// <summary>
/// Token ngắn hạn, ký HMAC, cho phép xem/stream MỘT bản ghi buổi học cụ thể.
/// Dùng thay cho Authorization header ở endpoint stream, vì thẻ &lt;video&gt;/trình phát gốc
/// không gửi được header tùy chỉnh (giống lý do SignalR hub nhận token qua query string).
/// Stateless — không lưu DB, tự hết hạn theo thời gian phát hành.
/// </summary>
public interface IRecordingAccessTokenService
{
    /// <summary>Phát hành token cho phép xem recording của classSessionId, gắn với userId, hết hạn sau lifetime.</summary>
    string Issue(int classSessionId, string userId, TimeSpan lifetime);

    /// <summary>Xác thực token khớp đúng classSessionId và còn hạn. Trả về userId đã phát hành nếu hợp lệ.</summary>
    bool TryValidate(string token, int classSessionId, out string? userId);
}
