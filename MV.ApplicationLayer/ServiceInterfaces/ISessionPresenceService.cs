namespace MV.ApplicationLayer.ServiceInterfaces;

/// <summary>
/// Theo dõi sự hiện diện (presence) của người dùng trong phòng học Agora của từng buổi.
/// Lưu in-memory: mỗi lần client heartbeat sẽ cập nhật mốc "last seen". Coi là "đang có
/// mặt" nếu heartbeat gần nhất nằm trong <see cref="PresenceWindowSeconds"/> giây.
///
/// Bảo mật: presence luôn được ghi dưới danh tính (UserId từ JWT) của CHÍNH người gọi
/// heartbeat, nên không ai có thể "khai hộ" sự có mặt của người khác. Đây là điều kiện
/// để auto check-in phản ánh đúng việc cả gia sư lẫn học viên thật sự vào phòng.
/// </summary>
public interface ISessionPresenceService
{
    /// <summary>Cập nhật mốc hiện diện của <paramref name="userId"/> cho buổi học.</summary>
    void Heartbeat(int classSessionId, string userId);

    /// <summary>Xoá hiện diện của <paramref name="userId"/> khi họ rời phòng.</summary>
    void Leave(int classSessionId, string userId);

    /// <summary>True nếu <paramref name="userId"/> có heartbeat trong cửa sổ hiện diện.</summary>
    bool IsPresent(int classSessionId, string userId);

    // ── Lobby (phòng chờ trước khi vào lớp) ──────────────────────────────────
    // Khác presence phòng học (heartbeat theo thời gian), lobby bám theo vòng đời
    // SignalR connection: vào khi JoinLobby, ra khi disconnect/LeaveLobby. Nhờ đó
    // "đang chờ trong lobby" là real-time và KHÔNG kích hoạt auto check-in (check-in
    // chỉ dựa trên presence phòng học).

    /// <summary>Ghi nhận <paramref name="userId"/> đang chờ trong lobby của buổi học qua một SignalR connection.</summary>
    void JoinLobby(int classSessionId, string userId, string connectionId);

    /// <summary>
    /// Gỡ một connection khỏi lobby (khi disconnect hoặc rời chủ động).
    /// Trả về (classSessionId, userId) mà connection đó đang chờ, hoặc null nếu không có.
    /// </summary>
    (int ClassSessionId, string UserId)? RemoveLobbyConnection(string connectionId);

    /// <summary>Đọc (classSessionId, userId) mà một connection đang chờ trong lobby, không gỡ. Null nếu không có.</summary>
    (int ClassSessionId, string UserId)? GetLobbyEntry(string connectionId);

    /// <summary>True nếu <paramref name="userId"/> có ít nhất một connection đang chờ trong lobby của buổi học.</summary>
    bool IsInLobby(int classSessionId, string userId);
}
