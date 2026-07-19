using System.Collections.Concurrent;
using MV.ApplicationLayer.ServiceInterfaces;

namespace MV.ApplicationLayer.Services;

/// <summary>
/// Bản cài đặt in-memory của <see cref="ISessionPresenceService"/> (đăng ký Singleton).
/// Presence là dữ liệu tạm thời: nếu app restart, các client vẫn heartbeat lại mỗi ~20s
/// nên trạng thái tự phục hồi trong vòng một chu kỳ. Không cần bảng DB.
/// </summary>
public class SessionPresenceService : ISessionPresenceService
{
    /// <summary>Cửa sổ hiện diện: heartbeat cũ hơn mốc này coi như đã rời phòng.</summary>
    private const int PresenceWindowSeconds = 60;

    private readonly ConcurrentDictionary<(int ClassSessionId, string UserId), DateTime> _lastSeen = new();

    /// <summary>
    /// Lobby: connectionId → (buổi học, user) đang chờ. Bám vòng đời SignalR connection nên
    /// một user mở nhiều tab vẫn đúng (mỗi tab một connection; còn ≥1 connection là còn chờ).
    /// </summary>
    private readonly ConcurrentDictionary<string, (int ClassSessionId, string UserId)> _lobbyConnections = new();

    public void Heartbeat(int classSessionId, string userId)
    {
        if (string.IsNullOrEmpty(userId)) return;
        _lastSeen[(classSessionId, userId)] = DateTime.UtcNow;
    }

    public void Leave(int classSessionId, string userId)
    {
        if (string.IsNullOrEmpty(userId)) return;
        _lastSeen.TryRemove((classSessionId, userId), out _);
    }

    public bool IsPresent(int classSessionId, string userId)
    {
        if (string.IsNullOrEmpty(userId)) return false;
        return _lastSeen.TryGetValue((classSessionId, userId), out var lastSeen)
            && DateTime.UtcNow - lastSeen <= TimeSpan.FromSeconds(PresenceWindowSeconds);
    }

    public void JoinLobby(int classSessionId, string userId, string connectionId)
    {
        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(connectionId)) return;
        _lobbyConnections[connectionId] = (classSessionId, userId);
    }

    public (int ClassSessionId, string UserId)? RemoveLobbyConnection(string connectionId)
    {
        if (string.IsNullOrEmpty(connectionId)) return null;
        return _lobbyConnections.TryRemove(connectionId, out var entry) ? entry : null;
    }

    public (int ClassSessionId, string UserId)? GetLobbyEntry(string connectionId)
    {
        if (string.IsNullOrEmpty(connectionId)) return null;
        return _lobbyConnections.TryGetValue(connectionId, out var entry) ? entry : null;
    }

    public bool IsInLobby(int classSessionId, string userId)
    {
        if (string.IsNullOrEmpty(userId)) return false;
        foreach (var entry in _lobbyConnections.Values)
        {
            if (entry.ClassSessionId == classSessionId && entry.UserId == userId)
                return true;
        }
        return false;
    }
}
