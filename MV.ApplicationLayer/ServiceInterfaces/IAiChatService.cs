using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;

namespace MV.ApplicationLayer.ServiceInterfaces;

public interface IAiChatService
{
    /// <summary>
    /// Hỏi AI giải toán trong một phiên: lưu user message → dựng history từ DB →
    /// gọi tutora-ai /solve → stream từng dòng SSE về cho caller (yield) →
    /// lưu assistant message khi stream xong. .NET là cổng duy nhất.
    /// </summary>
    IAsyncEnumerable<string> SolveStreamAsync(
        string userId, Guid sessionId, AiSolveRequest dto, CancellationToken ct = default);

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
}
