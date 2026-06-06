using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.Common.Hubs;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.DTO.RequestModel;

namespace MV.ApplicationLayer.Hubs
{
    [Authorize]
    public class ChatHub : BaseHub
    {
        private readonly ILogger<ChatHub> _logger;
        private readonly IServiceProvider _serviceProvider;

        public ChatHub(ILogger<ChatHub> logger, IServiceProvider serviceProvider) : base(logger, serviceProvider)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
        }

        public async Task JoinChannel(int channelId)
        {
            var groupName = $"channel:{channelId}";
            await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
            _logger.LogInformation("User {UserId} joined channel {ChannelId}", CurrentUserId, channelId);
        }

        public async Task LeaveChannel(int channelId)
        {
            var groupName = $"channel:{channelId}";
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
            _logger.LogInformation("User {UserId} left channel {ChannelId}", CurrentUserId, channelId);
        }

        public async Task Typing(int channelId)
        {
            if (string.IsNullOrEmpty(CurrentUserId))
                return;

            var groupName = $"channel:{channelId}";
            await Clients.OthersInGroup(groupName).SendAsync("userTyping", new
            {
                userId = CurrentUserId,
                channelId
            });
        }

        public async Task StopTyping(int channelId)
        {
            if (string.IsNullOrEmpty(CurrentUserId))
                return;

            var groupName = $"channel:{channelId}";
            await Clients.OthersInGroup(groupName).SendAsync("userStoppedTyping", new
            {
                userId = CurrentUserId,
                channelId
            });
        }

        public async Task SendMessage(int channelId, string content)
        {
            if (string.IsNullOrEmpty(CurrentUserId))
            {
                _logger.LogWarning("Attempt to send message without valid user authentication");
                throw new HubException("User not authenticated");
            }

            var dto = new ChatMessageCreateRequest { Content = content };

            using var scope = _serviceProvider.CreateScope();
            var chatService = scope.ServiceProvider.GetRequiredService<IChatService>();

            _logger.LogInformation("User {UserId} sending message to channel {ChannelId}", CurrentUserId, channelId);

            await chatService.SendMessageAsync(CurrentUserId, channelId, dto);

            _logger.LogInformation("Message sent to channel {ChannelId}", channelId);
        }
    }
}
