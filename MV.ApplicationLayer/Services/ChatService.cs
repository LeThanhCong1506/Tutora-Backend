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
    IStudentRepository studentRepo,
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
            CreatedAt = m.Createdat,
            Metadata = string.IsNullOrEmpty(m.Metadata) ? null : JsonSerializer.Deserialize<object>(m.Metadata),
            IsRead = m.Isread ?? false,
            ReadAt = m.Readat
        }).ToList();

        return new PagedList<ChatMessageResponse>(messageDtos, total, page, pageSize);
    }

    public async Task<List<ChatChannelListItemResponse>> GetMyChannelsAsync(string userId)
    {
        var channels = await chatRepo.GetChannelsByUserAsync(userId);

        var resolved = channels.Select(channel =>
        {
            string otherUserId;
            string? otherUserName;
            string? otherUserAvatarUrl;
            string otherUserRole;

            if (channel.Parentid == userId || channel.Studentid == userId)
            {
                otherUserId = channel.Tutorid ?? string.Empty;
                otherUserName = channel.Tutor?.Fullname;
                otherUserAvatarUrl = channel.Tutor?.Avatarurl;
                otherUserRole = UserRole.Tutor;
            }
            else if (channel.Studentid != null)
            {
                otherUserId = channel.Studentid;
                otherUserName = channel.Student?.Fullname;
                otherUserAvatarUrl = channel.Student?.Avatarurl;
                otherUserRole = UserRole.Student;
            }
            else
            {
                otherUserId = channel.Parentid ?? string.Empty;
                otherUserName = channel.Parent?.Fullname;
                otherUserAvatarUrl = channel.Parent?.Avatarurl;
                otherUserRole = UserRole.Parent;
            }

            return (channel, otherUserId, otherUserName, otherUserAvatarUrl, otherUserRole);
        }).ToList();

        // Batch-resolve which "Student" other-users actually have a linked Parent account,
        // so the label can distinguish self-registered students from parent-managed ones.
        var studentIdsToCheck = resolved
            .Where(r => r.otherUserRole == UserRole.Student)
            .Select(r => r.otherUserId)
            .ToList();
        var parentManagedIds = await studentRepo.GetParentManagedUserIdsAsync(studentIdsToCheck);

        var result = new List<ChatChannelListItemResponse>();
        foreach (var (channel, otherUserId, otherUserName, otherUserAvatarUrl, otherUserRole) in resolved)
        {
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
                OtherUserRole = otherUserRole,
                IsOtherUserParentManaged = otherUserRole == UserRole.Student
                    ? parentManagedIds.Contains(otherUserId)
                    : null,
                Status = channel.Status,
                LastMessageAt = channel.Lastmessageat,
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
            Createdat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow,
            Metadata = metadataJson
        };

        channel.Lastmessageat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;

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
            CreatedAt = message.Createdat,
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
            var senderName = sender?.Fullname ?? sender?.Username ?? "Tin nhắn mới";
            var preview = string.IsNullOrWhiteSpace(message.Content)
                ? "Bạn có tin nhắn mới trong cuộc trò chuyện"
                : message.Content.Length > 120
                    ? $"{message.Content[..120]}..."
                    : message.Content;
            var recipientIds = new[] { channel.Parentid, channel.Tutorid, channel.Studentid }
                .Where(id => !string.IsNullOrEmpty(id) && id != userId)
                .ToList();

            foreach (var recipientId in recipientIds)
            {
                await notificationService.CreateNotificationAsync(new NotificationRequest
                {
                    Userid = recipientId!,
                    Title = "Tin nhắn mới",
                    Message = $"{senderName}: {preview}",
                    Type = NotificationType.Message,
                    Referenceid = channelId.ToString()
                });
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send chat notification for channel {ChannelId}", channelId);
        }

        return response;
    }

    public async Task SendMeetLinksAsync(int bookingId, List<ClassSessionMiniResponse> classSessions)
    {
        var booking = await bookingRepo.FindWithStudentAsync(bookingId);
        if (booking == null) return;

        var channel = await chatRepo.FindChannelByParticipantsAsync(
            booking.Tutorid!, booking.Parentid, null);
        if (channel == null) return;

                var senderId = booking.Tutorid ?? SystemActors.System;

        // Chỉ gửi link BUỔI ĐẦU vào chat. Các buổi sau chỉ mở sau khi phụ huynh
        // thanh toán đợt 2 — gửi sẵn link mọi buổi sẽ khiến phụ huynh tưởng vào được
        // ngay (dù Agora token đã chặn ở BE). Link buổi sau sẽ được gửi khi tutor
        // check-in từng buổi.
        var firstSession = classSessions
            .Where(l => !string.IsNullOrWhiteSpace(l.MeetingLink))
            .OrderBy(l => l.ScheduledStart)
            .FirstOrDefault();

        if (firstSession != null)
        {
            await SendMessageAsync(senderId, channel.Channelid, new ChatMessageCreateRequest
            {
                Content = $"Buổi học đầu tiên: {firstSession.ScheduledStart:dd/MM HH:mm} - {firstSession.ScheduledEnd:HH:mm}\n\n🔗 Link tham gia: {firstSession.MeetingLink}",
                MessageType = ChatMessageType.MeetLink
            });
        }
    }

    public async Task<int> GetOrCreateChannelAsync(string parentId, string tutorId, bool isStudent = false)
    {
        var existing = await chatRepo.FindChannelByParticipantsAsync(tutorId,
            isStudent ? null : parentId,
            isStudent ? parentId : null);

        if (existing != null)
        {
            if (existing.Status == ChatChannelStatus.Closed)
            {
                existing.Status = ChatChannelStatus.Active;
                chatRepo.UpdateChannel(existing);
                await chatRepo.SaveChangesAsync();
            }

            return existing.Channelid;
        }

        var channel = new Chatchannel
        {
            Tutorid = tutorId,
            Status = ChatChannelStatus.Active,
            Createdat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow
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

    public async Task<int> GetOrCreateChannelForBookingAsync(string userId, int bookingId)
    {
        var booking = await bookingRepo.FindByIdForUserAsync(bookingId, userId)
            ?? throw new BookingException(
                BookingErrorCodes.BookingNotFound,
                "Không tìm thấy booking hoặc bạn không có quyền truy cập.",
                404);

        if (string.IsNullOrWhiteSpace(booking.Parentid) || string.IsNullOrWhiteSpace(booking.Tutorid))
            throw new BookingException(
                BookingErrorCodes.InvalidBookingStatus,
                "Booking chưa có đủ thông tin phụ huynh và gia sư để tạo cuộc trò chuyện.",
                400);

        return await GetOrCreateChannelAsync(booking.Parentid, booking.Tutorid);
    }

    public Task<int> GetUnreadTotalCountAsync(string userId)
        => chatRepo.GetUnreadTotalCountAsync(userId);

    public async Task MarkMessagesAsReadAsync(string userId, int channelId)
    {
        var unread = await chatRepo.GetUnreadMessagesAsync(channelId, userId);
        if (unread.Count == 0) return;

        var now = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
        foreach (var msg in unread)
        {
            msg.Isread = true;
            msg.Readat = now;
        }

        await chatRepo.SaveChangesAsync();
    }
}
