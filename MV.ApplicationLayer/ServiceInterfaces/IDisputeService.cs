using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;

namespace MV.ApplicationLayer.ServiceInterfaces;

/// <summary>
/// Service interface for admin dispute management
/// </summary>
public interface IDisputeService
{
    /// <summary>
    /// Get list of disputes with filters
    /// </summary>
    Task<PagedList<DisputeListResponse>> GetDisputesAsync(DisputeQueryRequest query);

    /// <summary>
    /// Get dispute detail with full context. <paramref name="actorId"/> = admin/staff hiện đang xem,
    /// dùng để phát hành token stream cho bản ghi video (nếu có) gắn kèm trong ClassSession.RecordingUrl.
    /// </summary>
    Task<DisputeDetailResponse?> GetDisputeDetailAsync(int disputeId, string actorId);

    /// <summary>
    /// Lấy thông tin bản ghi video (trạng thái + link stream tạm) của buổi học gắn với tranh chấp.
    /// Dùng cho Admin/Staff khi xử lý tranh chấp. RecordingUrl trỏ tới endpoint proxy có token
    /// ngắn hạn — KHÔNG phải link Drive trực tiếp (file trên Drive luôn ở chế độ private).
    /// </summary>
    Task<DisputeRecordingResponse> GetDisputeRecordingAsync(int disputeId, string actorId);

    /// <summary>
    /// Get chat history for a booking (dispute context)
    /// </summary>
    Task<List<ChatMessageResponse>> GetDisputeChatHistoryAsync(int disputeId);

    /// <summary>
    /// Mark dispute as investigating
    /// </summary>
    Task<DisputeDetailResponse> InvestigateDisputeAsync(int disputeId, string adminId);

    /// <summary>
    /// Resolve dispute with decision
    /// </summary>
    Task<DisputeDetailResponse> ResolveDisputeAsync(int disputeId, string adminId, ResolveDisputeRequest request);

    /// <summary>
    /// Get dispute statistics for admin dashboard
    /// </summary>
    Task<DisputeStatsResponse> GetDisputeStatsAsync();
}
