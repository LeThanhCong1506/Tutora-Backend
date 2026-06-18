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

    // Messages (chat_histories)
    Task<(IReadOnlyList<ChatHistory> Items, int Total)> GetMessagesPagedAsync(
        Guid sessionId, int page, int pageSize);
    void AddMessage(ChatHistory message);

    Task<int> SaveChangesAsync();
}
