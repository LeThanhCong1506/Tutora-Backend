using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;

namespace MV.ApplicationLayer.ServiceInterfaces;

public interface IAiChatService
{

    IAsyncEnumerable<string> SolveStreamAsync(
        string userId, Guid sessionId, AiSolveRequest dto, CancellationToken ct = default);

    Task<AssistantRespondResponse> RespondAsync(
        string? userId, AssistantRespondRequest dto, CancellationToken ct = default);

    /// <summary>Tạo phiên chat AI mới cho người dùng.</summary>
    Task<AiChatSessionResponse> CreateSessionAsync(string userId, AiChatSessionCreateRequest dto);

    /// <summary>Danh sách phiên chat AI của người dùng, lọc theo sessionType nếu có.</summary>
    Task<List<AiChatSessionResponse>> GetMySessionsAsync(string userId, string? sessionType = null);

    /// <summary>Lịch sử tin nhắn (phân trang) của một phiên — chỉ chủ phiên mới xem được.</summary>
    Task<PagedList<AiChatMessageResponse>> GetMessagesAsync(string userId, Guid sessionId, int page, int pageSize);

    /// <summary>Ghi một tin nhắn (user/assistant/system) vào phiên; cập nhật updatedAt.</summary>
    Task<AiChatMessageResponse> AddMessageAsync(string userId, Guid sessionId, AiChatMessageCreateRequest dto);

    /// <summary>Xoá một phiên chat AI (cascade xoá tin nhắn) — chỉ chủ phiên.</summary>
    Task DeleteSessionAsync(string userId, Guid sessionId);

    /// <summary>Xoá TẤT CẢ phiên chat AI của user. Trả về số phiên đã xoá.</summary>
    Task<int> DeleteAllSessionsAsync(string userId, string? sessionType = null);

    /// <summary>
    /// Đánh giá một câu trả lời của AI (like/dislike kèm lý do).
    /// </summary>
    Task VoteMessageAsync(string userId, Guid messageId, AiMessageVoteRequest dto, CancellationToken ct = default);
}
