using MV.DomainLayer.Entities;

namespace MV.ApplicationLayer.RepositoryInterfaces;

public interface IChatRepository
{
    // Channels
    Task<Chatchannel?> FindChannelByIdAsync(int channelId);
    Task<Chatchannel?> FindChannelByIdWithBookingAsync(int channelId);
    Task<Chatchannel?> FindChannelByParticipantsAsync(string tutorId, string? parentId, string? studentId);
    Task<List<Chatchannel>> GetChannelsByUserAsync(string userId);
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
