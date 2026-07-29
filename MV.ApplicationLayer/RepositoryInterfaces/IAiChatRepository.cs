using MV.DomainLayer.Entities;

namespace MV.ApplicationLayer.RepositoryInterfaces;

public interface IAiChatRepository
{
    // Sessions
    Task<ChatSession?> FindSessionByIdAsync(Guid sessionId);
    Task<List<ChatSession>> GetSessionsByUserAsync(string userId, string? sessionType = null);
    void AddSession(ChatSession session);
    void UpdateSession(ChatSession session);
    void RemoveSession(ChatSession session);
    Task<int> RemoveSessionsByUserAsync(string userId, string? sessionType = null);

    // Messages (chat_histories)
    Task<(IReadOnlyList<ChatHistory> Items, int Total)> GetMessagesPagedAsync(
        Guid sessionId, int page, int pageSize);
    void AddMessage(ChatHistory message);

    void AddTopicSignal(StudentTopicSignal signal);

    // Đánh giá lời giải
    Task<bool> IsMessageOwnedByUserAsync(Guid messageId, string userId);

    Task<AiMessageVote?> FindMessageVoteAsync(Guid messageId, string userId);

    Task<Dictionary<Guid, short>> GetMyVotesAsync(IEnumerable<Guid> messageIds, string userId);

    void AddMessageVote(AiMessageVote vote);

    Task<int> SaveChangesAsync();
}
