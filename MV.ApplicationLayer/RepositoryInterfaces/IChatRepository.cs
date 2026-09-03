using MV.DomainLayer.Entities;

namespace MV.ApplicationLayer.RepositoryInterfaces;

public interface IChatRepository
{
    // Channels
    Task<Chatchannel?> FindChannelByIdAsync(int channelId);
    Task<Chatchannel?> FindChannelByIdWithBookingAsync(int channelId);
    Task<Chatchannel?> FindChannelByParticipantsAsync(string tutorId, string? parentId, string? studentId);
    Task<List<Chatchannel>> GetChannelsByUserAsync(string userId);

    /// <summary>Ẩn kênh phía một người ("xoá phía tôi"); phía kia không đổi.</summary>
    Task HideChannelForUserAsync(int channelId, string userId);
    /// <summary>Kiểm tra quyền tham gia kênh bằng một truy vấn Any phía máy chủ.</summary>
    Task<bool> IsChannelParticipantAsync(int channelId, string userId);
    /// <summary>Kiểm tra hai người có kênh chat đang hoạt động hay không.</summary>
    Task<bool> AreActiveChatPartnersAsync(string userId, string targetUserId);
    /// <summary>
    /// Lọc tập user được yêu cầu xuống self và các đối tác chat đang hoạt động.
    /// Kết quả distinct được thực hiện phía cơ sở dữ liệu.
    /// </summary>
    Task<List<string>> GetAuthorizedPresenceUserIdsAsync(
        string requesterUserId,
        IReadOnlyCollection<string> requestedUserIds);
    /// <summary>Danh sách UserId distinct của các đối tác trong kênh chat đang hoạt động. Dùng cho broadcast presence.</summary>
    Task<List<string>> GetChatPartnerUserIdsAsync(string userId);
    void AddChannel(Chatchannel channel);
    void UpdateChannel(Chatchannel channel);

    // Messages
    Task<(IReadOnlyList<Chatmessage> Items, int Total)> GetMessagesPagedAsync(
        int channelId, int page, int pageSize, string? searchQuery = null);
    Task<List<Chatmessage>> GetUnreadMessagesAsync(int channelId, string senderId);
    /// <summary>Total unread messages across all channels the user participates in.</summary>
    Task<int> GetUnreadTotalCountAsync(string userId);
    void AddMessage(Chatmessage message);

    Task<int> SaveChangesAsync();
}
