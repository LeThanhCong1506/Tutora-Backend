using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.Interfaces;
using System.Security.Claims;

namespace MV.ApplicationLayer.Common.Hubs
{
    public abstract class BaseHub : Hub
    {
        protected string? CurrentUserId => Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

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

                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                    await unitOfWork.UserRepository.UpdateLastLoginAtAsync(CurrentUserId, MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to update last logout time for user {UserId}", CurrentUserId);
                }
            }
            else
            {
                _logger.LogDebug("Disconnected connection had no tracked user. ConnectionId: {ConnectionId}", Context.ConnectionId);
            }

            await base.OnDisconnectedAsync(exception);
        }

        protected async Task SendToUserAsync<T>(string userId, string method, T payload)
        {
            await Clients.Group($"user:{userId}").SendAsync(method, payload);
        }
    }
}
