using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.Hubs;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Entities;
using MV.DomainLayer.Exceptions;
using MV.DomainLayer.Helpers;
using MV.ApplicationLayer.RepositoryInterfaces;
using System.Text.Json;

namespace MV.ApplicationLayer.Services;

public class ChatService(
    IChatRepository chatRepo,
    IUserRepository userRepo,
    IBookingRepository bookingRepo,
    IHubContext<ChatHub> hubContext,
    INotificationService notificationService,
    ILogger<ChatService> logger) : IChatService
{
    public async Task<PagedList<ChatMessageResponse>> GetMessagesAsync(string userId, int channelId, int page, int pageSize, string? searchQuery = null)
    {
        var channel = await chatRepo.FindChannelByIdAsync(channelId)
            ?? throw new ChannelNotFoundException(channelId);

        if (channel.Parentid != userId && channel.Tutorid != userId && channel.Studentid != userId)
            throw new InvalidMessageException("Bạn không có quyền truy cập kênh trò chuyện này.");

        var (items, total) = await chatRepo.GetMessagesPagedAsync(channelId, page, pageSize, searchQuery);

        var messageDtos = items.Select(m => new ChatMessageResponse
        {
            MessageId = m.Messageid,
            ChannelId = m.Channelid ?? 0,
            SenderId = m.Senderid ?? string.Empty,
            SenderName = m.Sender?.Fullname,
            SenderAvatarUrl = m.Sender?.Avatarurl,
            Content = m.Content ?? string.Empty,
            MessageType = m.Messagetype ?? ChatMessageType.Text,
            CreatedAt = m.Createdat.HasValue ? VietnamTimeHelper.ToVietnamTime(m.Createdat.Value) : (DateTime?)null,
            Metadata = string.IsNullOrEmpty(m.Metadata) ? null : JsonSerializer.Deserialize<object>(m.Metadata),
            IsRead = m.Isread ?? false,
            ReadAt = m.Readat.HasValue ? VietnamTimeHelper.ToVietnamTime(m.Readat.Value) : (DateTime?)null
        }).ToList();

        return new PagedList<ChatMessageResponse>(messageDtos, total, page, pageSize);
    }

    public async Task<List<ChatChannelListItemResponse>> GetMyChannelsAsync(string userId)
    {
        var channels = await chatRepo.GetChannelsByUserAsync(userId);
        var result = new List<ChatChannelListItemResponse>();

        foreach (var channel in channels)
        {
            string otherUserId;
            string? otherUserName;
            string? otherUserAvatarUrl;

            if (channel.Parentid == userId || channel.Studentid == userId)
            {
                otherUserId = channel.Tutorid ?? string.Empty;
                otherUserName = channel.Tutor?.Fullname;
                otherUserAvatarUrl = channel.Tutor?.Avatarurl;
            }
            else
            {
                otherUserId = (channel.Studentid ?? channel.Parentid) ?? string.Empty;
                otherUserName = channel.Student?.Fullname ?? channel.Parent?.Fullname;
                otherUserAvatarUrl = channel.Student?.Avatarurl ?? channel.Parent?.Avatarurl;
            }

            var lastMessage = channel.Chatmessages.FirstOrDefault();
            var lastMessagePreview = lastMessage?.Content?.Length > 100
                ? lastMessage.Content[..100]
                : lastMessage?.Content;

            // Count messages sent by others that the current user hasn't read yet
            var unreadCount = channel.Chatmessages
                .Count(m => m.Senderid != userId && (m.Isread == null || m.Isread == false));

            result.Add(new ChatChannelListItemResponse
            {
                ChannelId = channel.Channelid,
                BookingId = channel.Bookingid,
                OtherUserId = otherUserId,
                OtherUserName = otherUserName,
                OtherUserAvatarUrl = otherUserAvatarUrl,
                Status = channel.Status,
                LastMessageAt = channel.Lastmessageat.HasValue ? VietnamTimeHelper.ToVietnamTime(channel.Lastmessageat.Value) : (DateTime?)null,
                LastMessagePreview = lastMessagePreview,
                UnreadCount = unreadCount
            });
        }

        return result;
    }

    public async Task<ChatMessageResponse> SendMessageAsync(string userId, int channelId, ChatMessageCreateRequest dto)
    {
        var channel = await chatRepo.FindChannelByIdAsync(channelId)
            ?? throw new ChannelNotFoundException(channelId);

        if (channel.Parentid != userId && channel.Tutorid != userId && channel.Studentid != userId)
            throw new InvalidMessageException("Bạn không có quyền gửi tin nhắn vào kênh này.");

        if (channel.Status == ChatChannelStatus.Closed)
            throw new InvalidMessageException("Kênh trò chuyện đã bị đóng. Không thể gửi tin nhắn.");

        var metadataJson = dto.Metadata != null ? JsonSerializer.Serialize(dto.Metadata) : null;

        var message = new Chatmessage
        {
            Channelid = channelId,
            Senderid = userId,
            Content = dto.Content,
            Messagetype = dto.MessageType ?? ChatMessageType.Text,
            Createdat = MV.DomainLayer.Helpers.VietnamTimeHelper.Now,
            Metadata = metadataJson
        };

        channel.Lastmessageat = MV.DomainLayer.Helpers.VietnamTimeHelper.Now;

        chatRepo.AddMessage(message);
        chatRepo.UpdateChannel(channel);
        await chatRepo.SaveChangesAsync();

        var sender = await userRepo.GetUserByIdAsync(userId);
        var response = new ChatMessageResponse
        {
            MessageId = message.Messageid,
            ChannelId = message.Channelid ?? 0,
            SenderId = message.Senderid ?? string.Empty,
            SenderName = sender?.Fullname,
            SenderAvatarUrl = sender?.Avatarurl,
            Content = message.Content ?? string.Empty,
            MessageType = message.Messagetype ?? ChatMessageType.Text,
            CreatedAt = message.Createdat.HasValue ? VietnamTimeHelper.ToVietnamTime(message.Createdat.Value) : (DateTime?)null,
            Metadata = dto.Metadata,
            IsRead = false,
            ReadAt = null
        };

        _ = Task.Run(async () =>
        {
            try
            {
                await hubContext.Clients.Group($"channel:{channelId}")
                    .SendAsync("messageReceived", response);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to send real-time message to channel {ChannelId}", channelId);
            }
        });

        try
        {
            var recipientIds = new[] { channel.Parentid, channel.Tutorid, channel.Studentid }
                .Where(id => !string.IsNullOrEmpty(id) && id != userId)
                .ToList();

            foreach (var recipientId in recipientIds)
            {
                await notificationService.CreateNotificationAsync(new NotificationRequest
                {
                    Userid = recipientId!,
                    Title = "Tin nhắn mới",
                    Message = "Bạn có tin nhắn mới trong cuộc trò chuyện"
                });
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send chat notification for channel {ChannelId}", channelId);
        }

        return response;
    }

    public async Task SendMeetLinksAsync(int bookingId, List<LessonMiniResponse> lessons)
    {
        var booking = await bookingRepo.FindWithStudentAsync(bookingId);
        if (booking == null) return;

        var channel = await chatRepo.FindChannelByParticipantsAsync(
            booking.Tutorid!, booking.Parentid, null);
        if (channel == null) return;

                var senderId = booking.Tutorid ?? SystemActors.System;

        var meetLinks = lessons
            .Where(l => !string.IsNullOrWhiteSpace(l.MeetingLink))
            .Select((l, i) => new ChatMessageCreateRequest
            {
                Content = $"📅 Buổi học {i + 1}: {l.ScheduledStart:dd/MM HH:mm} - {l.ScheduledEnd:HH:mm}\n\n🔗 Link tham gia: {l.MeetingLink}",
                MessageType = ChatMessageType.MeetLink
            })
            .ToList();

        foreach (var msgDto in meetLinks)
        {
            await SendMessageAsync(senderId, channel.Channelid, msgDto);
        }
    }

    public async Task<int> GetOrCreateChannelAsync(string parentId, string tutorId, bool isStudent = false)
    {
        var existing = await chatRepo.FindChannelByParticipantsAsync(tutorId,
            isStudent ? null : parentId,
            isStudent ? parentId : null);

        if (existing != null) return existing.Channelid;

        var channel = new Chatchannel
        {
            Tutorid = tutorId,
            Status = ChatChannelStatus.Active,
            Createdat = MV.DomainLayer.Helpers.VietnamTimeHelper.Now
        };

        if (isStudent) channel.Studentid = parentId;
        else channel.Parentid = parentId;

        var firstUser = await userRepo.GetUserByIdAsync(parentId);
        var tutorUser = await userRepo.GetUserByIdAsync(tutorId);

        if (firstUser != null) channel.Users.Add(firstUser);
        if (tutorUser != null) channel.Users.Add(tutorUser);

        chatRepo.AddChannel(channel);
        await chatRepo.SaveChangesAsync();
        return channel.Channelid;
    }

    public Task<int> GetUnreadTotalCountAsync(string userId)
        => chatRepo.GetUnreadTotalCountAsync(userId);

    public async Task MarkMessagesAsReadAsync(string userId, int channelId)
    {
        var unread = await chatRepo.GetUnreadMessagesAsync(channelId, userId);
        if (unread.Count == 0) return;

        var now = MV.DomainLayer.Helpers.VietnamTimeHelper.Now;
        foreach (var msg in unread)
        {
            msg.Isread = true;
            msg.Readat = now;
        }

        await chatRepo.SaveChangesAsync();
    }
}
