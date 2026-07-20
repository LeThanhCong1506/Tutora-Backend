using System.Diagnostics.Metrics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.Hubs;
using MV.ApplicationLayer.RepositoryInterfaces;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Helpers;
using StackExchange.Redis;

namespace MV.ApplicationLayer.Services;

/// <summary>
/// Shared policy for the Redis connection leases used by presence.
/// The client heartbeat is intentionally much shorter than the lease, so a short
/// browser/network pause does not make the user flicker offline.
/// </summary>
public static class PresenceLeasePolicy
{
    public static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(25);
    public static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(75);
    public static readonly TimeSpan CleanupInterval = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan LeaseKeyRetention = TimeSpan.FromMinutes(5);

    public const int CleanupBatchSize = 200;
    public const int MaxCleanupBatchesPerPass = 5;
    public const int MaxBatchLookupSize = 200;
}

/// <summary>
/// Lua scripts are public so contract tests can guard the atomic Redis operations.
/// Every script that changes a user's state touches the lease set, the global
/// active-user index and the version hash in one Redis operation.
/// </summary>
public static class PresenceRedisScripts
{
    public const string RegisterOrRefresh = """
        local previousScore = redis.call('ZSCORE', KEYS[2], ARGV[3])
        local wasOnline = previousScore ~= false
            and tonumber(previousScore) > tonumber(ARGV[1])
        local epoch = redis.call('GET', KEYS[4])
        if not epoch then
            redis.call('SET', KEYS[4], ARGV[6], 'NX')
            epoch = redis.call('GET', KEYS[4])
        end
        redis.call('ZREMRANGEBYSCORE', KEYS[1], '-inf', ARGV[1])
        redis.call('ZADD', KEYS[1], ARGV[2], ARGV[4])
        redis.call('PEXPIRE', KEYS[1], ARGV[5])

        local latest = redis.call('ZREVRANGE', KEYS[1], 0, 0, 'WITHSCORES')
        redis.call('ZADD', KEYS[2], latest[2], ARGV[3])

        local version = tonumber(redis.call('HGET', KEYS[3], ARGV[3]) or '0')
        local transition = 0
        if not wasOnline then
            version = redis.call('HINCRBY', KEYS[3], ARGV[3], 1)
            transition = 1
        end

        return { 1, transition, version, epoch }
        """;

    public const string Remove = """
        local wasOnline = redis.call('ZSCORE', KEYS[2], ARGV[2]) ~= false
        local epoch = redis.call('GET', KEYS[4])
        if not epoch then
            redis.call('SET', KEYS[4], ARGV[5], 'NX')
            epoch = redis.call('GET', KEYS[4])
        end
        redis.call('ZREM', KEYS[1], ARGV[3])
        redis.call('ZREMRANGEBYSCORE', KEYS[1], '-inf', ARGV[1])

        local count = redis.call('ZCARD', KEYS[1])
        local version = tonumber(redis.call('HGET', KEYS[3], ARGV[2]) or '0')
        local transition = 0

        if count == 0 then
            redis.call('DEL', KEYS[1])
            redis.call('ZREM', KEYS[2], ARGV[2])
            if wasOnline then
                version = redis.call('HINCRBY', KEYS[3], ARGV[2], 1)
                transition = -1
            end
            return { 0, transition, version, epoch }
        end

        local latest = redis.call('ZREVRANGE', KEYS[1], 0, 0, 'WITHSCORES')
        redis.call('ZADD', KEYS[2], latest[2], ARGV[2])
        redis.call('PEXPIRE', KEYS[1], ARGV[4])
        if not wasOnline then
            version = redis.call('HINCRBY', KEYS[3], ARGV[2], 1)
            transition = 1
        end
        return { 1, transition, version, epoch }
        """;

    /// <summary>
    /// Prunes one user and observes the resulting state. This is used both by a
    /// single-user read and by the background cleanup worker.
    /// </summary>
    public const string ObserveAndPrune = """
        local wasOnline = redis.call('ZSCORE', KEYS[2], ARGV[2]) ~= false
        local epoch = redis.call('GET', KEYS[4])
        if not epoch then
            redis.call('SET', KEYS[4], ARGV[4], 'NX')
            epoch = redis.call('GET', KEYS[4])
        end
        redis.call('ZREMRANGEBYSCORE', KEYS[1], '-inf', ARGV[1])

        local count = redis.call('ZCARD', KEYS[1])
        local version = tonumber(redis.call('HGET', KEYS[3], ARGV[2]) or '0')
        local transition = 0

        if count == 0 then
            redis.call('DEL', KEYS[1])
            redis.call('ZREM', KEYS[2], ARGV[2])
            if wasOnline then
                version = redis.call('HINCRBY', KEYS[3], ARGV[2], 1)
                transition = -1
            end
            return { 0, transition, version, epoch }
        end

        local latest = redis.call('ZREVRANGE', KEYS[1], 0, 0, 'WITHSCORES')
        redis.call('ZADD', KEYS[2], latest[2], ARGV[2])
        redis.call('PEXPIRE', KEYS[1], ARGV[3])
        if not wasOnline then
            version = redis.call('HINCRBY', KEYS[3], ARGV[2], 1)
            transition = 1
        end
        return { 1, transition, version, epoch }
        """;

    /// <summary>
    /// Reads all requested users from the global state index in one Lua/Redis
    /// round-trip. The flattened result contains the state epoch followed by
    /// online/version pairs.
    /// </summary>
    public const string BatchRead = """
        local epoch = redis.call('GET', KEYS[3])
        if not epoch then
            redis.call('SET', KEYS[3], ARGV[2], 'NX')
            epoch = redis.call('GET', KEYS[3])
        end

        local result = { epoch }
        for i = 3, #ARGV do
            local score = redis.call('ZSCORE', KEYS[1], ARGV[i])
            local online = score ~= false and tonumber(score) > tonumber(ARGV[1])
            local version = tonumber(redis.call('HGET', KEYS[2], ARGV[i]) or '0')
            table.insert(result, online and 1 or 0)
            table.insert(result, version)
        end
        return result
        """;
}

/// <summary>
/// Presence backed by exact Redis connection leases rather than an aggregate
/// counter. Connection add/remove and state transitions are atomic and safe when
/// reconnect overlaps a stale disconnect on another app instance.
/// </summary>
public sealed class PresenceService : IPresenceService
{
    private const int TransitionOnline = 1;
    private const int TransitionOffline = -1;

    private static readonly Meter Meter = new("Tutora.Presence", "2.0");
    private static readonly Counter<long> TransitionCounter =
        Meter.CreateCounter<long>("tutora.presence.transitions");
    private static readonly Counter<long> RedisUnavailableCounter =
        Meter.CreateCounter<long>("tutora.presence.redis_unavailable");
    private static readonly Counter<long> ExpiredLeaseCounter =
        Meter.CreateCounter<long>("tutora.presence.expired_users");

    private readonly IConnectionMultiplexer _redis;
    private readonly IUserRepository _userRepo;
    private readonly IChatRepository _chatRepo;
    private readonly IHubContext<NotificationHub> _hub;
    private readonly ILogger<PresenceService> _logger;
    private readonly string _keyPrefix;
    private readonly string _epochCandidate = Guid.NewGuid().ToString("N");

    public PresenceService(
        IConnectionMultiplexer redis,
        IUserRepository userRepo,
        IChatRepository chatRepo,
        IHubContext<NotificationHub> hub,
        IConfiguration configuration,
        IHostEnvironment environment,
        ILogger<PresenceService> logger)
    {
        _redis = redis;
        _userRepo = userRepo;
        _chatRepo = chatRepo;
        _hub = hub;
        _logger = logger;

        var configuredNamespace = configuration["Presence:RedisNamespace"];
        var appNamespace = string.IsNullOrWhiteSpace(configuredNamespace)
            ? "tutora"
            : SanitizeKeyPart(configuredNamespace);
        var environmentName = SanitizeKeyPart(environment.EnvironmentName);

        // The hash tag keeps all keys touched by one Lua script in the same slot
        // when Redis Cluster is enabled. Environment remains part of the tag so
        // staging and production never share presence state.
        _keyPrefix =
            $"{appNamespace}:{environmentName}:{{{appNamespace}-{environmentName}-presence}}:v2";
    }

    public Task RegisterConnectionAsync(string userId, string connectionId) =>
        RegisterOrRefreshAsync(userId, connectionId, "register");

    public Task RefreshConnectionAsync(string userId, string connectionId) =>
        RegisterOrRefreshAsync(userId, connectionId, "heartbeat");

    public async Task RemoveConnectionAsync(string userId, string connectionId)
    {
        if (!HasIdentity(userId, connectionId))
            return;

        try
        {
            var now = TimeZoneHelper.UtcNow;
            var result = await Database.ScriptEvaluateAsync(
                PresenceRedisScripts.Remove,
                StateKeys(userId),
                [
                    ToUnixMilliseconds(now),
                    userId,
                    connectionId,
                    (long)PresenceLeasePolicy.LeaseKeyRetention.TotalMilliseconds,
                    _epochCandidate
                ]);

            await HandleTransitionAsync(userId, ParseLeaseResult(result), now);
        }
        catch (RedisException ex)
        {
            RecordRedisUnavailable("remove", userId, ex);
            // The lease expires naturally; do not report a false offline state.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Presence lease removal failed for user {UserId}, connection {ConnectionId}",
                userId, connectionId);
        }
    }

    public async Task<UserPresenceResponse> GetPresenceAsync(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return Unknown(userId);

        try
        {
            var now = TimeZoneHelper.UtcNow;
            var result = await Database.ScriptEvaluateAsync(
                PresenceRedisScripts.ObserveAndPrune,
                StateKeys(userId),
                [
                    ToUnixMilliseconds(now),
                    userId,
                    (long)PresenceLeasePolicy.LeaseKeyRetention.TotalMilliseconds,
                    _epochCandidate
                ]);

            var lease = ParseLeaseResult(result);
            await HandleTransitionAsync(userId, lease, now);
            return await BuildResponseAsync(
                userId,
                lease.IsOnline,
                lease.Version,
                lease.Epoch,
                includeLastSeen: true);
        }
        catch (RedisException ex)
        {
            RecordRedisUnavailable("read", userId, ex);
            return Unknown(userId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Presence read failed for user {UserId}", userId);
            return Unknown(userId);
        }
    }

    public async Task<IReadOnlyList<UserPresenceResponse>> GetPresencesAsync(
        IReadOnlyCollection<string> userIds,
        bool includeLastSeen = false)
    {
        var ids = userIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (ids.Length == 0)
            return [];
        if (ids.Length > PresenceLeasePolicy.MaxBatchLookupSize)
            throw new ArgumentOutOfRangeException(
                nameof(userIds),
                $"Presence batch is limited to {PresenceLeasePolicy.MaxBatchLookupSize} users.");

        try
        {
            var result = await Database.ScriptEvaluateAsync(
                PresenceRedisScripts.BatchRead,
                [ActiveUsersKey, VersionsKey, EpochKey],
                new RedisValue[]
                    {
                        ToUnixMilliseconds(TimeZoneHelper.UtcNow),
                        _epochCandidate
                    }
                    .Concat(ids.Select(id => (RedisValue)id))
                    .ToArray());
            var values = AsArray(result);

            if (values.Length != ids.Length * 2 + 1)
                throw new RedisServerException("Unexpected presence batch script result.");

            var epoch = AsString(values[0]);
            var responses = new List<UserPresenceResponse>(ids.Length);
            for (var index = 0; index < ids.Length; index++)
            {
                var isOnline = AsInt64(values[index * 2 + 1]) == 1;
                var version = AsInt64(values[index * 2 + 2]);
                responses.Add(await BuildResponseAsync(
                    ids[index], isOnline, version, epoch, includeLastSeen));
            }

            return responses;
        }
        catch (RedisException ex)
        {
            RecordRedisUnavailable("batch-read", userId: null, ex);
            return ids.Select(Unknown).ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Presence batch read failed for {UserCount} users", ids.Length);
            return ids.Select(Unknown).ToArray();
        }
    }

    public async Task<int> CleanupExpiredLeasesAsync(CancellationToken cancellationToken = default)
    {
        var offlineTransitions = 0;

        try
        {
            for (var batchIndex = 0;
                 batchIndex < PresenceLeasePolicy.MaxCleanupBatchesPerPass;
                 batchIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var now = TimeZoneHelper.UtcNow;
                var candidates = await Database.SortedSetRangeByScoreAsync(
                    ActiveUsersKey,
                    stop: ToUnixMilliseconds(now),
                    order: Order.Ascending,
                    take: PresenceLeasePolicy.CleanupBatchSize);

                if (candidates.Length == 0)
                    break;

                var batch = Database.CreateBatch();
                var pending = new List<(string UserId, Task<RedisResult> Result)>(candidates.Length);
                foreach (var value in candidates)
                {
                    var userId = (string?)value;
                    if (string.IsNullOrWhiteSpace(userId))
                        continue;

                    pending.Add((
                        userId,
                        batch.ScriptEvaluateAsync(
                            PresenceRedisScripts.ObserveAndPrune,
                            StateKeys(userId),
                            [
                                ToUnixMilliseconds(now),
                                userId,
                                (long)PresenceLeasePolicy.LeaseKeyRetention.TotalMilliseconds,
                                _epochCandidate
                            ])));
                }

                batch.Execute();

                foreach (var item in pending)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var lease = ParseLeaseResult(await item.Result);
                    if (lease.Transition == TransitionOffline)
                    {
                        offlineTransitions++;
                        await HandleTransitionAsync(item.UserId, lease, now);
                    }
                    else if (lease.Transition == TransitionOnline)
                    {
                        // Repairs an inconsistent index without suppressing the
                        // corresponding online event.
                        await HandleTransitionAsync(item.UserId, lease, now);
                    }
                }

                if (candidates.Length < PresenceLeasePolicy.CleanupBatchSize)
                    break;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (RedisException ex)
        {
            RecordRedisUnavailable("cleanup", userId: null, ex);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Presence lease cleanup pass failed");
        }

        if (offlineTransitions > 0)
        {
            ExpiredLeaseCounter.Add(offlineTransitions);
            _logger.LogInformation(
                "Presence cleanup moved {UserCount} expired users offline",
                offlineTransitions);
        }

        return offlineTransitions;
    }

    private IDatabase Database => _redis.GetDatabase();

    private RedisKey ActiveUsersKey => $"{_keyPrefix}:active-users";

    private RedisKey VersionsKey => $"{_keyPrefix}:versions";

    private RedisKey EpochKey => $"{_keyPrefix}:epoch";

    private async Task RegisterOrRefreshAsync(
        string userId,
        string connectionId,
        string operation)
    {
        if (!HasIdentity(userId, connectionId))
            return;

        try
        {
            var now = TimeZoneHelper.UtcNow;
            var expiresAt = now.Add(PresenceLeasePolicy.LeaseDuration);
            var result = await Database.ScriptEvaluateAsync(
                PresenceRedisScripts.RegisterOrRefresh,
                StateKeys(userId),
                [
                    ToUnixMilliseconds(now),
                    ToUnixMilliseconds(expiresAt),
                    userId,
                    connectionId,
                    (long)PresenceLeasePolicy.LeaseKeyRetention.TotalMilliseconds,
                    _epochCandidate
                ]);

            await HandleTransitionAsync(userId, ParseLeaseResult(result), now);
        }
        catch (RedisException ex)
        {
            RecordRedisUnavailable(operation, userId, ex);
            // A later heartbeat retries registration and can recover the state.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Presence {Operation} failed for user {UserId}, connection {ConnectionId}",
                operation, userId, connectionId);
        }
    }

    private async Task HandleTransitionAsync(
        string userId,
        LeaseScriptResult result,
        DateTime changedAt)
    {
        if (result.Transition == 0)
            return;

        var isOnline = result.Transition == TransitionOnline;
        DateTime? lastSeenAt = null;

        if (!isOnline)
        {
            lastSeenAt = changedAt;
            try
            {
                await _userRepo.UpdateLastSeenAtAsync(userId, changedAt);
            }
            catch (Exception ex)
            {
                // Do not suppress the state event when the durable timestamp
                // write fails; the monotonic repository update is retry-safe.
                _logger.LogWarning(ex,
                    "Failed to persist last-seen for user {UserId} at {LastSeenAt}",
                    userId, changedAt);
            }
        }

        TransitionCounter.Add(
            1,
            new KeyValuePair<string, object?>(
                "state",
                isOnline ? UserPresenceStatus.Online : UserPresenceStatus.Offline));

        _logger.LogInformation(
            "Presence transition for user {UserId}: {Status}, version {Version}",
            userId,
            isOnline ? UserPresenceStatus.Online : UserPresenceStatus.Offline,
            result.Version);

        await BroadcastPresenceAsync(
            userId,
            isOnline,
            lastSeenAt,
            result.Version,
            result.Epoch,
            changedAt);
    }

    private async Task<UserPresenceResponse> BuildResponseAsync(
        string userId,
        bool isOnline,
        long version,
        string epoch,
        bool includeLastSeen)
    {
        DateTime? lastSeen = null;
        if (!isOnline && includeLastSeen)
        {
            try
            {
                var user = await _userRepo.GetUserByIdAsync(userId);
                lastSeen = user?.Lastseenat;
            }
            catch (Exception ex)
            {
                // Redis is still authoritative for online/offline. A temporary
                // PostgreSQL failure should omit last-seen, not turn a known
                // offline state into "unknown".
                _logger.LogWarning(ex,
                    "Failed to read last-seen for offline user {UserId}",
                    userId);
            }
        }

        return new UserPresenceResponse
        {
            UserId = userId,
            IsOnline = isOnline,
            Status = isOnline ? UserPresenceStatus.Online : UserPresenceStatus.Offline,
            Version = version,
            Epoch = epoch,
            LastSeenAt = lastSeen
        };
    }

    private async Task BroadcastPresenceAsync(
        string userId,
        bool isOnline,
        DateTime? lastSeenAt,
        long version,
        string epoch,
        DateTime changedAt)
    {
        try
        {
            var partnerIds = await _chatRepo.GetChatPartnerUserIdsAsync(userId);
            if (partnerIds.Count == 0)
                return;

            var groupNames = partnerIds
                .Select(partnerId => $"user:{partnerId}")
                .Distinct(StringComparer.Ordinal)
                .ToList();
            var payload = new
            {
                userId,
                isOnline,
                status = isOnline ? UserPresenceStatus.Online : UserPresenceStatus.Offline,
                lastSeenAt,
                version,
                epoch,
                changedAt
            };

            await _hub.Clients.Groups(groupNames).SendAsync("presenceChanged", payload);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Presence broadcast failed for user {UserId}, version {Version}",
                userId, version);
        }
    }

    private RedisKey[] StateKeys(string userId) =>
        [LeaseKey(userId), ActiveUsersKey, VersionsKey, EpochKey];

    private RedisKey LeaseKey(string userId)
    {
        var userHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(userId)));
        return $"{_keyPrefix}:user:{userHash}:leases";
    }

    private void RecordRedisUnavailable(
        string operation,
        string? userId,
        Exception exception)
    {
        RedisUnavailableCounter.Add(
            1,
            new KeyValuePair<string, object?>("operation", operation));
        _logger.LogWarning(
            exception,
            "Presence Redis unavailable during {Operation} for user {UserId}",
            operation,
            userId);
    }

    private static UserPresenceResponse Unknown(string? userId) => new()
    {
        UserId = userId ?? string.Empty,
        IsOnline = null,
        Status = UserPresenceStatus.Unknown,
        Version = 0,
        Epoch = null,
        LastSeenAt = null
    };

    private static bool HasIdentity(string userId, string connectionId) =>
        !string.IsNullOrWhiteSpace(userId)
        && !string.IsNullOrWhiteSpace(connectionId);

    private static long ToUnixMilliseconds(DateTime value) =>
        new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc))
            .ToUnixTimeMilliseconds();

    private static string SanitizeKeyPart(string value)
    {
        var sanitized = new string(value
            .Trim()
            .ToLowerInvariant()
            .Select(character =>
                char.IsLetterOrDigit(character) || character is '-' or '_'
                    ? character
                    : '-')
            .ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "default" : sanitized;
    }

    private static LeaseScriptResult ParseLeaseResult(RedisResult result)
    {
        var values = AsArray(result);
        if (values.Length != 4)
            throw new RedisServerException("Unexpected presence lease script result.");

        return new LeaseScriptResult(
            IsOnline: AsInt64(values[0]) == 1,
            Transition: (int)AsInt64(values[1]),
            Version: AsInt64(values[2]),
            Epoch: AsString(values[3]));
    }

    private static RedisResult[] AsArray(RedisResult result)
    {
        var values = (RedisResult[]?)result;
        return values
            ?? throw new RedisServerException("Unexpected presence script result.");
    }

    private static long AsInt64(RedisResult result) => (long)result;

    private static string AsString(RedisResult result) =>
        result.IsNull ? string.Empty : (string?)result ?? string.Empty;

    private sealed record LeaseScriptResult(
        bool IsOnline,
        int Transition,
        long Version,
        string Epoch);
}

/// <summary>
/// Periodically expires abandoned browser/server leases. Multiple application
/// instances may run this worker safely because the cleanup Lua script owns the
/// online → offline transition atomically.
/// </summary>
public sealed class PresenceLeaseCleanupService(
    IServiceScopeFactory scopeFactory,
    ILogger<PresenceLeaseCleanupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PresenceLeasePolicy.CleanupInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var presence = scope.ServiceProvider.GetRequiredService<IPresenceService>();
                    await presence.CleanupExpiredLeasesAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // A transient scope/DI failure must not permanently kill the
                    // cleanup worker; the next interval retries.
                    logger.LogWarning(ex, "Presence cleanup worker iteration failed");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogDebug("Presence lease cleanup stopped");
        }
    }
}
