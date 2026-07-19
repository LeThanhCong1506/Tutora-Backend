using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.Common.Hubs;
using MV.ApplicationLayer.Helpers;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;

namespace MV.ApplicationLayer.Hubs;

/// <summary>
/// Persistent signaling channel for a device that owns a live-session lease.
/// A takeover targets the old lease group and asks that client to leave Agora immediately.
/// </summary>
[Authorize]
public sealed class LiveSessionHub(
    ILogger<LiveSessionHub> logger,
    IServiceProvider serviceProvider,
    ILiveSessionDeviceLeaseService leases) : BaseHub(logger, serviceProvider)
{
    private const string RegisteredGroupItem = "live-session-lease-group";

    public static string LeaseGroup(string leaseId) => $"live-session:lease:{leaseId}";

    public async Task RegisterSession(int classSessionId, string participationId, string leaseId)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId))
            throw new HubException("User not authenticated");

        if (!LiveSessionDeviceInputValidator.TryNormalizeGuid(participationId, out var normalizedParticipationId)
            || !LiveSessionDeviceInputValidator.TryNormalizeGuid(leaseId, out var normalizedLeaseId))
        {
            throw new HubException(LiveSessionDeviceErrorCodes.InvalidDeviceSession);
        }

        if (!await leases.IsActiveAsync(
                classSessionId,
                userId,
                normalizedParticipationId,
                normalizedLeaseId,
                Context.ConnectionAborted))
        {
            throw new HubException(LiveSessionDeviceErrorCodes.LeaseRevoked);
        }

        if (Context.Items.TryGetValue(RegisteredGroupItem, out var previousValue)
            && previousValue is string previousGroup)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, previousGroup);
        }

        var group = LeaseGroup(normalizedLeaseId);
        await Groups.AddToGroupAsync(Context.ConnectionId, group);

        // Close the check/add race: takeover may commit after the first Redis check but before
        // SignalR adds this connection. A second authoritative check keeps a revoked connection
        // from silently registering after it already missed the takeover broadcast.
        if (!await leases.IsActiveAsync(
                classSessionId,
                userId,
                normalizedParticipationId,
                normalizedLeaseId,
                Context.ConnectionAborted))
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, group);
            throw new HubException(LiveSessionDeviceErrorCodes.LeaseRevoked);
        }

        Context.Items[RegisteredGroupItem] = group;

        logger.LogInformation(
            "Registered live-session signaling for user {UserId}, classSession {ClassSessionId}",
            userId, classSessionId);
    }
}
