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

    public Task<ChatSession?> FindSessionByUserAndClassSessionAsync(string userId, string sessionType, int classSessionId)
        => context.ChatSessions
            .FirstOrDefaultAsync(s => s.UserId == userId && s.SessionType == sessionType && s.ClassSessionId == classSessionId);

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

    // Tín hiệu chủ đề (student_topic_signals)
    // Xoá phiên -> DB tự cascade theo FK session_id
    public void AddTopicSignal(StudentTopicSignal signal)
        => context.StudentTopicSignals.Add(signal);

    // Đánh giá lời giải (ai_message_votes)
    public Task<bool> IsMessageOwnedByUserAsync(Guid messageId, string userId)
        => context.ChatHistories
            .AsNoTracking()
            .AnyAsync(m => m.MessageId == messageId
                           && context.ChatSessions.Any(s => s.SessionId == m.SessionId && s.UserId == userId));

    public Task<AiMessageVote?> FindMessageVoteAsync(Guid messageId, string userId)
        => context.AiMessageVotes
            .FirstOrDefaultAsync(v => v.MessageId == messageId && v.UserId == userId);

    public async Task<Dictionary<Guid, short>> GetMyVotesAsync(IEnumerable<Guid> messageIds, string userId)
    {
        var ids = messageIds as IReadOnlyCollection<Guid> ?? messageIds.ToList();
        if (ids.Count == 0) return new Dictionary<Guid, short>();

        return await context.AiMessageVotes
            .AsNoTracking()
            .Where(v => v.UserId == userId && ids.Contains(v.MessageId))
            .ToDictionaryAsync(v => v.MessageId, v => v.Vote);
    }

    public void AddMessageVote(AiMessageVote vote)
        => context.AiMessageVotes.Add(vote);

    public Task<int> SaveChangesAsync()
        => context.SaveChangesAsync();
}
