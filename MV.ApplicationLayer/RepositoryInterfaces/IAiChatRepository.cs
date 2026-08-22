using MV.DomainLayer.Entities;

namespace MV.ApplicationLayer.RepositoryInterfaces;

public interface IAiChatRepository
{
    // Sessions
    Task<ChatSession?> FindSessionByIdAsync(Guid sessionId);
    Task<List<ChatSession>> GetSessionsByUserAsync(string userId, string? sessionType = null);
    /// <summary>Tra phiên chat theo (user, loại phiên, buổi học) — dùng cho video_summary, mỗi (user, classSession) chỉ có 1 phiên.</summary>
    Task<ChatSession?> FindSessionByUserAndClassSessionAsync(string userId, string sessionType, int classSessionId);
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

    /// <summary>
    /// Xếp 1 câu hỏi mới vào questions với review_status='pending_review' (bánh đà bank).
    /// Bỏ qua nếu đã có câu trùng nội dung. Trả true nếu thật sự thêm mới.
    /// </summary>
    Task<bool> AddPendingQuestionAsync(
        string content, string solution, string? chapter, string? grade, string createdBy);

    Task<int> SaveChangesAsync();
}
