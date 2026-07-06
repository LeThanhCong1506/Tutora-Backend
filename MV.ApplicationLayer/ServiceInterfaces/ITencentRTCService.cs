namespace MV.ApplicationLayer.ServiceInterfaces;

/// <summary>
/// Thông tin phòng Tencent RTC để client join video call.
/// </summary>
public record TRTCRoomInfo(
    int RoomId,
    string UserId,
    string UserSig,
    int SdkAppId,
    int ExpireAt
);

/// <summary>
/// Service tạo UserSig cho Tencent RTC theo thuật toán HMAC-SHA256 của Tencent.
/// Tài liệu: https://trtc.io/document/35166
/// </summary>
public interface ITencentRTCService
{
    /// <summary>
    /// Tạo UserSig cho một userId, dùng để client join Tencent RTC room.
    /// </summary>
    /// <param name="userId">UserId của người dùng (tutor hoặc student)</param>
    /// <returns>Chuỗi UserSig đã ký</returns>
    string GenerateUserSig(string userId);

    /// <summary>
    /// Lấy đầy đủ thông tin để client join room của một buổi học.
    /// RoomId = classSessionId (deterministic, không cần external API).
    /// </summary>
    /// <param name="classSessionId">ID buổi học</param>
    /// <param name="userId">UserId của người dùng đang join</param>
    TRTCRoomInfo GetRoomInfo(int classSessionId, string userId);
}
