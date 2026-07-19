using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.ServiceInterfaces;
using System.Security.Claims;

namespace MV.ApplicationLayer.Common.Hubs
{
    public abstract class BaseHub : Hub
    {
        protected string? CurrentUserId => Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        protected virtual bool TracksPresence => false;

        private readonly ILogger<BaseHub> _logger;
        private readonly IServiceProvider _serviceProvider;

        protected BaseHub(ILogger<BaseHub> logger, IServiceProvider serviceProvider)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
        }

        public override async Task OnConnectedAsync()
        {
            if (!string.IsNullOrEmpty(CurrentUserId))
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, $"user:{CurrentUserId}");
                _logger.LogInformation("User {UserId} connected with ConnectionId {ConnectionId}", CurrentUserId, Context.ConnectionId);

                if (TracksPresence)
                {
                    try
                    {
                        using var scope = _serviceProvider.CreateScope();
                        var presence = scope.ServiceProvider.GetRequiredService<IPresenceService>();
                        await presence.RegisterConnectionAsync(CurrentUserId, Context.ConnectionId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex,
                            "Failed to register presence lease for user {UserId}, connection {ConnectionId}",
                            CurrentUserId, Context.ConnectionId);
                    }
                }
            }
            else
            {
                _logger.LogWarning("A connection was established without a valid NameIdentifier claim. ConnectionId: {ConnectionId}", Context.ConnectionId);
            }

            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            if (!string.IsNullOrEmpty(CurrentUserId))
            {
                if (exception is null)
                {
                    _logger.LogInformation("User {UserId} disconnected", CurrentUserId);
                }
                else
                {
                    _logger.LogWarning(exception, "User {UserId} disconnected due to an exception", CurrentUserId);
                }

                if (TracksPresence)
                {
                    try
                    {
                        using var scope = _serviceProvider.CreateScope();
                        var presence = scope.ServiceProvider.GetRequiredService<IPresenceService>();
                        await presence.RemoveConnectionAsync(CurrentUserId, Context.ConnectionId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex,
                            "Failed to remove presence lease for user {UserId}, connection {ConnectionId}",
                            CurrentUserId, Context.ConnectionId);
                    }
                }
            }
            else
            {
                _logger.LogDebug("Disconnected connection had no tracked user. ConnectionId: {ConnectionId}", Context.ConnectionId);
            }

            await base.OnDisconnectedAsync(exception);
        }

        protected async Task RefreshPresenceLeaseAsync()
        {
            if (!TracksPresence || string.IsNullOrEmpty(CurrentUserId))
                return;

            using var scope = _serviceProvider.CreateScope();
            var presence = scope.ServiceProvider.GetRequiredService<IPresenceService>();
            await presence.RefreshConnectionAsync(CurrentUserId, Context.ConnectionId);
        }

        protected async Task SendToUserAsync<T>(string userId, string method, T payload)
        {
            await Clients.Group($"user:{userId}").SendAsync(method, payload);
        }
    }
}
