using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.Common.Hubs;
using MV.ApplicationLayer.RepositoryInterfaces;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.DTO.RequestModel;

namespace MV.ApplicationLayer.Hubs
{
    [Authorize]
    public class ChatHub : BaseHub
    {
        private const string AuthorizedChannelsItemKey = "chat:authorized-channels";
        private readonly ILogger<ChatHub> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly IChatRepository _chatRepository;

        public ChatHub(
            ILogger<ChatHub> logger,
            IServiceProvider serviceProvider,
            IChatRepository chatRepository) : base(logger, serviceProvider)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _chatRepository = chatRepository;
        }

        public async Task JoinChannel(int channelId)
        {
            var userId = await EnsureChannelParticipantAsync(channelId);

            var groupName = $"channel:{channelId}";
            await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
            _logger.LogInformation("User {UserId} joined channel {ChannelId}", userId, channelId);
        }

        public async Task LeaveChannel(int channelId)
        {
            var userId = await EnsureChannelParticipantAsync(channelId);

            var groupName = $"channel:{channelId}";
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
            _logger.LogInformation("User {UserId} left channel {ChannelId}", userId, channelId);
        }

        public async Task Typing(int channelId)
        {
            var userId = await EnsureChannelParticipantAsync(channelId);

            var groupName = $"channel:{channelId}";
            await Clients.OthersInGroup(groupName).SendAsync("userTyping", new
            {
                userId,
                channelId
            });
        }

        public async Task StopTyping(int channelId)
        {
            var userId = await EnsureChannelParticipantAsync(channelId);

            var groupName = $"channel:{channelId}";
            await Clients.OthersInGroup(groupName).SendAsync("userStoppedTyping", new
            {
                userId,
                channelId
            });
        }

        public async Task SendMessage(int channelId, string content)
        {
            var userId = await EnsureChannelParticipantAsync(channelId);

            var dto = new ChatMessageCreateRequest { Content = content };

            using var scope = _serviceProvider.CreateScope();
            var chatService = scope.ServiceProvider.GetRequiredService<IChatService>();

            _logger.LogInformation("User {UserId} sending message to channel {ChannelId}", userId, channelId);

            await chatService.SendMessageAsync(userId, channelId, dto);

            _logger.LogInformation("Message sent to channel {ChannelId}", channelId);
        }

        private async Task<string> EnsureChannelParticipantAsync(int channelId)
        {
            var userId = CurrentUserId;
            if (channelId <= 0 || string.IsNullOrWhiteSpace(userId))
                return RejectUnauthorizedOperation(userId);

            // Participant IDs on a chat channel are immutable. Cache a successful lookup for
            // this SignalR connection so typing events do not create a database query per keypress.
            if (!Context.Items.TryGetValue(AuthorizedChannelsItemKey, out var cachedValue) ||
                cachedValue is not HashSet<int> authorizedChannels)
            {
                authorizedChannels = [];
                Context.Items[AuthorizedChannelsItemKey] = authorizedChannels;
            }

            if (authorizedChannels.Contains(channelId))
                return userId;

            if (await _chatRepository.IsChannelParticipantAsync(channelId, userId))
            {
                authorizedChannels.Add(channelId);
                return userId;
            }

            // Cố ý dùng cùng một thông báo cho kênh không tồn tại và kênh không thuộc caller
            // để không biến Hub thành oracle dò channelId.
            return RejectUnauthorizedOperation(userId);
        }

        private string RejectUnauthorizedOperation(string? userId)
        {
            _logger.LogWarning(
                "Rejected unauthorized chat hub operation for user {UserId}",
                userId ?? "(anonymous)");
            throw new HubException("Không thể truy cập kênh trò chuyện.");
        }
    }
}
