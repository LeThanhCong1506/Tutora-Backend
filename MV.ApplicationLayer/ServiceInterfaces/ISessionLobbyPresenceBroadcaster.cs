namespace MV.ApplicationLayer.ServiceInterfaces;

/// <summary>
/// Tính lại lobbyState/scheduleChangeState/sessionReady cho một buổi học rồi đẩy tới group lobby
/// tương ứng trên <c>SessionLobbyHub</c>. Tách riêng khỏi Hub để những nơi làm presence đổi mà
/// KHÔNG đi qua lobby hub connection (ví dụ AgoraController.Heartbeat khi một bên đã vào thẳng
/// phòng học thật) cũng gọi được, thay vì chỉ trông chờ vào client tự poll RefreshState mỗi 10s.
/// </summary>
public interface ISessionLobbyPresenceBroadcaster
{
    Task BroadcastAsync(
        int classSessionId,
        string actorUserId,
        string? actorRole,
        CancellationToken cancellationToken = default);
}
