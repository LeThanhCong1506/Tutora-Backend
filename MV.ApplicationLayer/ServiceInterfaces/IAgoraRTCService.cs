namespace MV.ApplicationLayer.ServiceInterfaces;

/// <summary>
/// Thông tin phòng Agora RTC để client join video call.
/// </summary>
/// <param name="Channel">Tên kênh (channel name) riêng của buổi học.</param>
/// <param name="ClassSessionId">ID buổi học hiện tại — client dùng để gia hạn token / heartbeat presence.</param>
/// <param name="Uid">Agora user account (= UserId của người dùng). Client join channel bằng account này.</param>
/// <param name="Token">RTC token (AccessToken2, bắt đầu bằng "007").</param>
/// <param name="AppId">Agora App ID.</param>
/// <param name="ExpireAt">Thời điểm token hết hạn (Unix time, giây).</param>
public record AgoraRoomInfo(
    string Channel,
    int ClassSessionId,
    string Uid,
    string Token,
    string AppId,
    int ExpireAt
);

/// <summary>
/// Service tạo Agora RTC token (AccessToken2) để client join kênh video call.
/// Docs: https://docs.agora.io/en/video-calling/get-started/authentication-workflow
/// </summary>
public interface IAgoraRTCService
{
    /// <summary>
    /// Tạo RTC token cho một Agora user account trong một kênh cụ thể (role = Publisher).
    /// </summary>
    /// <param name="channelName">Tên kênh (channel).</param>
    /// <param name="account">Agora user account (string) — dùng chính UserId của người dùng.</param>
    /// <returns>Chuỗi token AccessToken2 (bắt đầu bằng "007").</returns>
    string GenerateToken(string channelName, string account);

    /// <summary>
    /// Lấy đầy đủ thông tin để client join kênh của một buổi học.
    /// Channel được cô lập theo classSessionId để khớp với scope lease và presence.
    /// </summary>
    /// <param name="classSessionId">ID buổi học hiện tại.</param>
    /// <param name="bookingId">ID booking chứa buổi học (giữ để tương thích contract hiện tại).</param>
    /// <param name="userId">UserId của người dùng đang join (dùng làm Agora user account).</param>
    AgoraRoomInfo GetRoomInfo(int classSessionId, int? bookingId, string userId);
}
