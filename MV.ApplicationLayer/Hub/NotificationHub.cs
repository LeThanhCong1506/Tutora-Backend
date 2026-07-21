using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.Common.Hubs;
using MV.ApplicationLayer.ServiceInterfaces;

namespace MV.ApplicationLayer.Hubs
{
    [Authorize]
    public class NotificationHub : BaseHub
    {
        protected override bool TracksPresence => true;

        private readonly ILogger<NotificationHub> _logger;
        private readonly INotificationService _notificationService;

        public NotificationHub(ILogger<NotificationHub> logger, INotificationService notificationService, IServiceProvider serviceProvider) : base(logger, serviceProvider)
        {
            _logger = logger;
            _notificationService = notificationService;
        }

        public async Task MarkNotificationAsRead(int notificationId)
        {
            _logger.LogInformation("User {UserId} marked notification {NotificationId} as read", CurrentUserId, notificationId);

            if (string.IsNullOrEmpty(CurrentUserId))
                return;

            // MarkAsReadAsync already pushes NotificationCountUpdated internally
            await _notificationService.MarkAsReadAsync(notificationId, CurrentUserId);

            await Clients.Caller.SendAsync("NotificationMarkedAsRead", notificationId);
        }

        /// <summary>
        /// Canonical system-presence heartbeat. The portal client invokes this
        /// every ~25 seconds; a lease is reclaimed after 75 seconds without one.
        /// </summary>
        public async Task PresenceHeartbeat()
        {
            if (string.IsNullOrEmpty(CurrentUserId))
                throw new HubException("User not authenticated");

            await RefreshPresenceLeaseAsync();
        }
    }
}
