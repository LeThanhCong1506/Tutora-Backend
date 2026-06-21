using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;

namespace MV.ApplicationLayer.ServiceInterfaces;

public interface IChatService
{
    /// <summary>
    /// Paged chat history for a channel; supports keyword search within messages.
    /// </summary>
    Task<PagedList<ChatMessageResponse>> GetMessagesAsync(string userId, int channelId, int page, int pageSize, string? searchQuery = null);

    /// <summary>
    /// All chat channels the calling user is a participant in, ordered by last activity.
    /// </summary>
    Task<List<ChatChannelListItemResponse>> GetMyChannelsAsync(string userId);

    /// <summary>
    /// Send a chat message to a channel on behalf of the calling user.
    /// </summary>
    Task<ChatMessageResponse> SendMessageAsync(string userId, int channelId, ChatMessageCreateRequest dto);

    /// <summary>
    /// Send meeting links for all auto-created lessons in a booking via the chat channel.
    /// Called by <c>ILessonService.AutoCreateLessonsAsync</c> after tutor acceptance.
    /// </summary>
    Task SendMeetLinksAsync(int bookingId, List<LessonMiniResponse> lessons);

    /// <summary>
    /// Return the existing chat channel between a parent/student and a tutor,
    /// or create one if it does not yet exist.
    /// </summary>
    Task<int> GetOrCreateChannelAsync(string parentOrStudentId, string tutorId, bool isStudent = false);

    /// <summary>
    /// Return the chat channel for a booking participant, creating it when needed.
    /// The caller must be the booking's parent, linked student, or tutor.
    /// </summary>
    Task<int> GetOrCreateChannelForBookingAsync(string userId, int bookingId);

    /// <summary>
    /// Mark all unread messages in a channel as read for the calling user.
    /// </summary>
    Task MarkMessagesAsReadAsync(string userId, int channelId);

    /// <summary>
    /// Total count of unread messages across all channels the calling user participates in.
    /// Lightweight — used for badge display only.
    /// </summary>
    Task<int> GetUnreadTotalCountAsync(string userId);
}
