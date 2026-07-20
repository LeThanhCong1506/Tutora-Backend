using Microsoft.EntityFrameworkCore;
using MV.DomainLayer.Entities;
using MV.InfrastructureLayer.DBContext;
using MV.ApplicationLayer.RepositoryInterfaces;

namespace MV.InfrastructureLayer.Repositories;

public class AiChatRepository(AgoraDbContext context) : IAiChatRepository
{
    // Sessions (chat_sessions)

    public Task<ChatSession?> FindSessionByIdAsync(Guid sessionId)
        => context.ChatSessions
            .FirstOrDefaultAsync(s => s.SessionId == sessionId);

    public Task<List<ChatSession>> GetSessionsByUserAsync(string userId, string? sessionType = null)
        => context.ChatSessions
            .Where(s => s.UserId == userId)
            .Where(s => string.IsNullOrEmpty(sessionType) || s.SessionType == sessionType)
            .OrderByDescending(s => s.UpdatedAt)
            .AsNoTracking()
            .ToListAsync();

    public void AddSession(ChatSession session)
        => context.ChatSessions.Add(session);

    public void UpdateSession(ChatSession session)
        => context.ChatSessions.Update(session);

    public void RemoveSession(ChatSession session)
        => context.ChatSessions.Remove(session);

    /// Xoá TẤT CẢ phiên chat AI của user (kèm toàn bộ tin nhắn). Trả về số phiên đã xoá.
    public async Task<int> RemoveSessionsByUserAsync(string userId, string? sessionType = null)
    {
        var sessionIds = await context.ChatSessions
            .Where(s => s.UserId == userId)
            .Where(s => string.IsNullOrEmpty(sessionType) || s.SessionType == sessionType)
            .Select(s => s.SessionId)
            .ToListAsync();

        if (sessionIds.Count == 0) return 0;

        await context.ChatHistories
            .Where(m => sessionIds.Contains(m.SessionId))
            .ExecuteDeleteAsync();

        return await context.ChatSessions
            .Where(s => sessionIds.Contains(s.SessionId))
            .ExecuteDeleteAsync();
    }

    // Messages (chat_histories)

    public async Task<(IReadOnlyList<ChatHistory> Items, int Total)> GetMessagesPagedAsync(
        Guid sessionId, int page, int pageSize)
    {
        var q = context.ChatHistories
            .Where(m => m.SessionId == sessionId)
            .OrderBy(m => m.CreatedAt)
            .AsNoTracking();

        var total = await q.CountAsync();
        var items = await q.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return (items, total);
    }

    public void AddMessage(ChatHistory message)
        => context.ChatHistories.Add(message);

    public Task<int> SaveChangesAsync()
        => context.SaveChangesAsync();
}
