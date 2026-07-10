namespace MV.ApplicationLayer.ServiceInterfaces;

/// <summary>
/// Trạng thái presence (in-memory, không bền vững qua restart) của một buổi học.
/// </summary>
public record ClassSessionPresenceState(
    DateTime VideoCallStartedAtUtc,
    long AccumulatedSeconds,
    bool IsCoPresenceConfirmed
);

/// <summary>
/// Theo dõi heartbeat của tutor/student trong kênh Agora để tính thời gian đồng hiện diện (co-presence).
/// Lưu tạm trong bộ nhớ (không ghi DB) — chỉ phục vụ test luồng UX, mất dữ liệu khi restart server.
/// </summary>
public interface IAgoraPresenceTracker
{
    /// <summary>
    /// Ghi nhận 1 heartbeat ping từ user (tutor hoặc student) cho một buổi học.
    /// </summary>
    /// <param name="classSessionId">ID buổi học.</param>
    /// <param name="isTutor">true nếu ping đến từ tutor, false nếu từ student.</param>
    /// <param name="micOn">Trạng thái mic hiện tại (chỉ giữ giá trị mới nhất, không log lịch sử).</param>
    /// <param name="camOn">Trạng thái camera hiện tại.</param>
    ClassSessionPresenceState RecordPing(int classSessionId, bool isTutor, bool micOn, bool camOn);

    /// <summary>
    /// Lấy trạng thái presence hiện tại của một buổi học, null nếu chưa có ping nào.
    /// </summary>
    ClassSessionPresenceState? GetState(int classSessionId);
}
