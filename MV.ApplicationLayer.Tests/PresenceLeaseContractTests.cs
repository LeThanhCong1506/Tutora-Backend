using System.Reflection;
using MV.ApplicationLayer.Common.Hubs;
using MV.ApplicationLayer.Hubs;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.ApplicationLayer.Services;
using MV.DomainLayer.DTO.ResponseModel;
using MV.PresentationLayer.Migrations;
using Xunit;

namespace MV.ApplicationLayer.Tests;

public class PresenceLeaseContractTests
{
    [Fact]
    public void LeasePolicy_HasHeartbeatAndBoundedFailureDetection()
    {
        Assert.InRange(
            PresenceLeasePolicy.LeaseDuration,
            TimeSpan.FromSeconds(60),
            TimeSpan.FromSeconds(90));
        Assert.True(
            PresenceLeasePolicy.HeartbeatInterval
            < PresenceLeasePolicy.LeaseDuration / 2);
        Assert.True(
            PresenceLeasePolicy.CleanupInterval
            < PresenceLeasePolicy.HeartbeatInterval);
        Assert.True(
            PresenceLeasePolicy.LeaseKeyRetention
            > PresenceLeasePolicy.LeaseDuration);
    }

    [Fact]
    public void RedisScripts_UseExactConnectionLeasesAndAtomicTransitions()
    {
        Assert.Contains("ZADD", PresenceRedisScripts.RegisterOrRefresh);
        Assert.Contains("PEXPIRE", PresenceRedisScripts.RegisterOrRefresh);
        Assert.Contains("HINCRBY", PresenceRedisScripts.RegisterOrRefresh);

        Assert.Contains("ZREM", PresenceRedisScripts.Remove);
        Assert.Contains("ARGV[3]", PresenceRedisScripts.Remove);
        Assert.Contains("ZREMRANGEBYSCORE", PresenceRedisScripts.ObserveAndPrune);
        Assert.Contains("HINCRBY", PresenceRedisScripts.ObserveAndPrune);
        Assert.Contains(
            "tonumber(score) > tonumber(ARGV[1])",
            PresenceRedisScripts.BatchRead);
        Assert.Contains("epoch", PresenceRedisScripts.BatchRead);

        var allScripts = string.Join(
            Environment.NewLine,
            PresenceRedisScripts.RegisterOrRefresh,
            PresenceRedisScripts.Remove,
            PresenceRedisScripts.ObserveAndPrune);
        Assert.DoesNotContain("DECR", allScripts, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("StringIncrement", allScripts, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PresenceContract_IsConnectionScopedAndSupportsBatchReads()
    {
        var methods = typeof(IPresenceService).GetMethods()
            .ToDictionary(method => method.Name, StringComparer.Ordinal);

        Assert.Equal(
            [typeof(string), typeof(string)],
            methods[nameof(IPresenceService.RegisterConnectionAsync)]
                .GetParameters()
                .Select(parameter => parameter.ParameterType));
        Assert.Equal(
            [typeof(string), typeof(string)],
            methods[nameof(IPresenceService.RefreshConnectionAsync)]
                .GetParameters()
                .Select(parameter => parameter.ParameterType));
        Assert.Equal(
            [typeof(string), typeof(string)],
            methods[nameof(IPresenceService.RemoveConnectionAsync)]
                .GetParameters()
                .Select(parameter => parameter.ParameterType));

        var batch = methods[nameof(IPresenceService.GetPresencesAsync)];
        Assert.Equal(
            typeof(IReadOnlyCollection<string>),
            batch.GetParameters()[0].ParameterType);
        Assert.Equal(typeof(bool), batch.GetParameters()[1].ParameterType);
    }

    [Fact]
    public void NotificationHub_IsTheOnlyCanonicalPresenceHub()
    {
        const BindingFlags declaredProtected =
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        Assert.NotNull(typeof(NotificationHub)
            .GetProperty("TracksPresence", declaredProtected));
        Assert.Null(typeof(ChatHub)
            .GetProperty("TracksPresence", declaredProtected));
        Assert.Null(typeof(SessionLobbyHub)
            .GetProperty("TracksPresence", declaredProtected));
        Assert.Null(typeof(LiveSessionHub)
            .GetProperty("TracksPresence", declaredProtected));
        Assert.NotNull(typeof(NotificationHub)
            .GetMethod(nameof(NotificationHub.PresenceHeartbeat)));
        Assert.NotNull(typeof(BaseHub)
            .GetProperty(
                "TracksPresence",
                BindingFlags.Instance | BindingFlags.NonPublic));
    }

    [Fact]
    public void UnknownPresence_CanBeRepresentedWithoutPretendingOffline()
    {
        var isOnlineProperty = typeof(UserPresenceResponse)
            .GetProperty(nameof(UserPresenceResponse.IsOnline));

        Assert.NotNull(isOnlineProperty);
        Assert.Equal(
            typeof(bool),
            Nullable.GetUnderlyingType(isOnlineProperty!.PropertyType));
        Assert.Equal("unknown", UserPresenceStatus.Unknown);
        Assert.NotNull(typeof(UserPresenceResponse)
            .GetProperty(nameof(UserPresenceResponse.Epoch)));
    }

    [Fact]
    public void LastSeenMigration_IsEmbeddedForDeploymentRunner()
    {
        var resources = typeof(ManagedMigrationRunner)
            .Assembly
            .GetManifestResourceNames();

        Assert.Contains(resources, resource =>
            resource.Contains(".ManagedMigrations.", StringComparison.Ordinal)
            && resource.EndsWith(
                "V20260719__user_presence_last_seen.sql",
                StringComparison.OrdinalIgnoreCase));
    }
}
