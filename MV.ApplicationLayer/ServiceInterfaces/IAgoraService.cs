namespace MV.ApplicationLayer.ServiceInterfaces;

/// <summary>
/// Thông tin phòng Agora RTC để client join video call.
/// </summary>
public record AgoraRoomInfo(
    string ChannelName,
    string UserId,
    string Token,
    string AppId,
    int ExpireAt
);

/// <summary>
/// Service tạo Agora RTC Token (AccessToken2) để client join video call.
/// Docs: https://docs.agora.io/en/video-calling/get-started/authentication-workflow
/// </summary>
public interface IAgoraService
{
    /// <summary>
    /// Tạo Agora token cho một userId + channelName.
    /// </summary>
    string GenerateToken(string channelName, string userId);

    /// <summary>
    /// Lấy đầy đủ thông tin để client join room của một buổi học.
    /// ChannelName = lessonId.ToString() (deterministic).
    /// </summary>
    AgoraRoomInfo GetRoomInfo(int lessonId, string userId);
}
