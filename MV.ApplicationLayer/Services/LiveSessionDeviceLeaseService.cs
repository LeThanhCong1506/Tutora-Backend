using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.ServiceInterfaces;
using StackExchange.Redis;

namespace MV.ApplicationLayer.Services;

/// <summary>
/// Redis-backed, cross-instance implementation of the live-session device lease.
/// Every mutation is a single Lua script so concurrent admission/takeover requests
/// cannot both become the active device.
/// </summary>
public sealed class LiveSessionDeviceLeaseService(
    IConnectionMultiplexer redis,
    ILogger<LiveSessionDeviceLeaseService> logger) : ILiveSessionDeviceLeaseService
{
    // This lease controls device ownership, not the short-lived classroom presence status.
    // Mobile browsers and Zalo can suspend JavaScript timers for several minutes while the app
    // is backgrounded, so a 90-second TTL could silently admit a second device without takeover.
    // Explicit leave/takeover still releases or replaces the lease immediately; this TTL is only
    // the crash-recovery safety net and comfortably covers a normal lesson.
    public const int LeaseTtlSeconds = 4 * 60 * 60;

    private static readonly RedisValue LeaseTtlMilliseconds =
        (long)TimeSpan.FromSeconds(LeaseTtlSeconds).TotalMilliseconds;

    private const string AdmitScript = """
        local activeParticipation = redis.call('HGET', KEYS[1], 'participationId')
        local activeLease = redis.call('HGET', KEYS[1], 'leaseId')

        if not activeParticipation or not activeLease then
            redis.call('DEL', KEYS[1])
            redis.call('HSET', KEYS[1],
                'participationId', ARGV[1],
                'leaseId', ARGV[2],
                'deviceId', ARGV[3],
                'deviceLabel', ARGV[4])
            redis.call('PEXPIRE', KEYS[1], ARGV[5])
            return { 1, ARGV[2], ARGV[3], ARGV[4] }
        end

        local activeDeviceId = redis.call('HGET', KEYS[1], 'deviceId') or ''
        if activeParticipation == ARGV[1] and activeDeviceId == ARGV[3] then
            redis.call('PEXPIRE', KEYS[1], ARGV[5])
            local activeDeviceLabel = redis.call('HGET', KEYS[1], 'deviceLabel') or ''
            return { 1, activeLease, activeDeviceId, activeDeviceLabel }
        end

        local activeDeviceLabel = redis.call('HGET', KEYS[1], 'deviceLabel') or ''
        return { 0, activeLease, '', activeDeviceLabel }
        """;

    private const string TakeoverScript = """
        local activeLease = redis.call('HGET', KEYS[1], 'leaseId')
        local activeParticipation = redis.call('HGET', KEYS[1], 'participationId')

        if not activeLease or not activeParticipation then
            redis.call('DEL', KEYS[1])
            redis.call('HSET', KEYS[1],
                'participationId', ARGV[1],
                'leaseId', ARGV[2],
                'deviceId', ARGV[3],
                'deviceLabel', ARGV[4])
            redis.call('PEXPIRE', KEYS[1], ARGV[6])
            return { 1, ARGV[2], '' }
        end

        local activeDeviceId = redis.call('HGET', KEYS[1], 'deviceId') or ''
        if activeParticipation == ARGV[1] and activeDeviceId == ARGV[3] then
            redis.call('PEXPIRE', KEYS[1], ARGV[6])
            return { 1, activeLease, '' }
        end

        if activeLease ~= ARGV[5] then
            local activeDeviceLabel = redis.call('HGET', KEYS[1], 'deviceLabel') or ''
            return { 0, activeLease or '', activeDeviceLabel }
        end

        redis.call('HSET', KEYS[1],
            'participationId', ARGV[1],
            'leaseId', ARGV[2],
            'deviceId', ARGV[3],
            'deviceLabel', ARGV[4])
        redis.call('PEXPIRE', KEYS[1], ARGV[6])
        return { 1, ARGV[2], activeLease }
        """;

    private const string RenewScript = """
        local activeParticipation = redis.call('HGET', KEYS[1], 'participationId')
        local activeLease = redis.call('HGET', KEYS[1], 'leaseId')
        if activeParticipation == ARGV[1] and activeLease == ARGV[2] then
            redis.call('PEXPIRE', KEYS[1], ARGV[3])
            return 1
        end
        return 0
        """;

    private const string IsActiveScript = """
        local activeParticipation = redis.call('HGET', KEYS[1], 'participationId')
        local activeLease = redis.call('HGET', KEYS[1], 'leaseId')
        if activeParticipation == ARGV[1] and activeLease == ARGV[2] then
            return 1
        end
        return 0
        """;

    private const string ReleaseScript = """
        local activeParticipation = redis.call('HGET', KEYS[1], 'participationId')
        local activeLease = redis.call('HGET', KEYS[1], 'leaseId')
        if activeParticipation == ARGV[1] and activeLease == ARGV[2] then
            redis.call('DEL', KEYS[1])
            return 1
        end
        return 0
        """;

    private IDatabase Database => redis.GetDatabase();

    public async Task<LiveSessionAdmissionResult> AdmitAsync(
        int classSessionId,
        string userId,
        string participationId,
        string deviceId,
        string deviceLabel,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var proposedLeaseId = Guid.NewGuid().ToString("N");
        var result = await Database.ScriptEvaluateAsync(
            AdmitScript,
            [BuildKey(classSessionId, userId)],
            [participationId, proposedLeaseId, deviceId, deviceLabel, LeaseTtlMilliseconds]);

        var values = AsArray(result);
        var admitted = AsInt64(values[0]) == 1;
        if (!admitted)
        {
            var activeLeaseId = AsString(values[1]);
            var activeDeviceLabel = AsString(values[3]);
            logger.LogInformation(
                "Rejected second device for user {UserId} in classSession {ClassSessionId}",
                userId, classSessionId);
            return new LiveSessionAdmissionResult(false, null, activeLeaseId, activeDeviceLabel);
        }

        var lease = new LiveSessionDeviceLease(
            participationId,
            AsString(values[1]),
            AsString(values[2]),
            AsString(values[3]));
        return new LiveSessionAdmissionResult(true, lease, null, null);
    }

    public async Task<LiveSessionTakeoverResult> TakeoverAsync(
        int classSessionId,
        string userId,
        string participationId,
        string deviceId,
        string deviceLabel,
        string expectedActiveLeaseId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var newLeaseId = Guid.NewGuid().ToString("N");
        var result = await Database.ScriptEvaluateAsync(
            TakeoverScript,
            [BuildKey(classSessionId, userId)],
            [
                participationId,
                newLeaseId,
                deviceId,
                deviceLabel,
                expectedActiveLeaseId,
                LeaseTtlMilliseconds
            ]);

        var values = AsArray(result);
        var takenOver = AsInt64(values[0]) == 1;
        if (!takenOver)
        {
            return new LiveSessionTakeoverResult(
                false,
                null,
                null,
                NullIfEmpty(AsString(values[1])),
                NullIfEmpty(AsString(values[2])));
        }

        var lease = new LiveSessionDeviceLease(participationId, AsString(values[1]), deviceId, deviceLabel);
        var replacedLeaseId = AsString(values[2]);
        logger.LogInformation(
            "Completed live-session takeover for user {UserId} in classSession {ClassSessionId}; replacedExisting={ReplacedExisting}",
            userId, classSessionId, !string.IsNullOrEmpty(replacedLeaseId));
        return new LiveSessionTakeoverResult(true, lease, replacedLeaseId, null, null);
    }

    public async Task<bool> RenewAsync(
        int classSessionId,
        string userId,
        string participationId,
        string leaseId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = await Database.ScriptEvaluateAsync(
            RenewScript,
            [BuildKey(classSessionId, userId)],
            [participationId, leaseId, LeaseTtlMilliseconds]);
        return AsInt64(result) == 1;
    }

    public async Task<bool> IsActiveAsync(
        int classSessionId,
        string userId,
        string participationId,
        string leaseId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = await Database.ScriptEvaluateAsync(
            IsActiveScript,
            [BuildKey(classSessionId, userId)],
            [participationId, leaseId]);
        return AsInt64(result) == 1;
    }

    public async Task<bool> ReleaseAsync(
        int classSessionId,
        string userId,
        string participationId,
        string leaseId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = await Database.ScriptEvaluateAsync(
            ReleaseScript,
            [BuildKey(classSessionId, userId)],
            [participationId, leaseId]);
        return AsInt64(result) == 1;
    }

    private static RedisKey BuildKey(int classSessionId, string userId)
    {
        var userHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(userId)));
        return $"tutora:live-session-device:{classSessionId}:{userHash}";
    }

    private static RedisResult[] AsArray(RedisResult result)
    {
        var values = (RedisResult[]?)result;
        return values ?? throw new RedisServerException("Unexpected live-session lease script result.");
    }

    private static long AsInt64(RedisResult result) => (long)result;

    private static string AsString(RedisResult result) =>
        result.IsNull ? string.Empty : (string?)result ?? string.Empty;

    private static string? NullIfEmpty(string value) =>
        string.IsNullOrEmpty(value) ? null : value;
}
