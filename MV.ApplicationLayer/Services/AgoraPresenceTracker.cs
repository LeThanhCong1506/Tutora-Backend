using System.Collections.Concurrent;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Helpers;

namespace MV.ApplicationLayer.Services;

/// <summary>
/// Implementation in-memory của <see cref="IAgoraPresenceTracker"/>.
/// Đăng ký như Singleton trong DI — state sống trong suốt vòng đời process, mất khi restart.
/// </summary>
public class AgoraPresenceTracker : IAgoraPresenceTracker
{
    private const int CoPresenceWindowSeconds = 60;
    private const long CoPresenceConfirmThresholdSeconds = 1800; // 30 phút

    private class Entry
    {
        public readonly object Lock = new();
        public DateTime VideoCallStartedAtUtc;
        public DateTime? LastTutorPingUtc;
        public DateTime? LastStudentPingUtc;
        public DateTime? LastAccumulationTickUtc;
        public long AccumulatedSeconds;
    }

    private readonly ConcurrentDictionary<int, Entry> _entries = new();

    public ClassSessionPresenceState RecordPing(int classSessionId, bool isTutor, bool micOn, bool camOn)
    {
        var entry = _entries.GetOrAdd(classSessionId, _ => new Entry
        {
            VideoCallStartedAtUtc = TimeZoneHelper.UtcNow
        });

        lock (entry.Lock)
        {
            var now = TimeZoneHelper.UtcNow;

            if (isTutor)
                entry.LastTutorPingUtc = now;
            else
                entry.LastStudentPingUtc = now;

            var bothRecentlyPresent =
                entry.LastTutorPingUtc.HasValue &&
                entry.LastStudentPingUtc.HasValue &&
                (now - entry.LastTutorPingUtc.Value).TotalSeconds <= CoPresenceWindowSeconds &&
                (now - entry.LastStudentPingUtc.Value).TotalSeconds <= CoPresenceWindowSeconds;

            if (bothRecentlyPresent)
            {
                if (entry.LastAccumulationTickUtc.HasValue)
                {
                    var elapsed = (now - entry.LastAccumulationTickUtc.Value).TotalSeconds;
                    // Chỉ cộng dồn nếu khoảng cách hợp lý (tránh cộng khống sau khi cả 2 vắng mặt lâu rồi quay lại)
                    if (elapsed > 0 && elapsed <= CoPresenceWindowSeconds)
                        entry.AccumulatedSeconds += (long)elapsed;
                }
                entry.LastAccumulationTickUtc = now;
            }
            else
            {
                entry.LastAccumulationTickUtc = null;
            }

            return new ClassSessionPresenceState(
                entry.VideoCallStartedAtUtc,
                entry.AccumulatedSeconds,
                entry.AccumulatedSeconds >= CoPresenceConfirmThresholdSeconds);
        }
    }

    public ClassSessionPresenceState? GetState(int classSessionId)
    {
        if (!_entries.TryGetValue(classSessionId, out var entry))
            return null;

        lock (entry.Lock)
        {
            return new ClassSessionPresenceState(
                entry.VideoCallStartedAtUtc,
                entry.AccumulatedSeconds,
                entry.AccumulatedSeconds >= CoPresenceConfirmThresholdSeconds);
        }
    }
}
