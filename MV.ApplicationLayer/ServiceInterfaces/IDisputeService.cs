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
    /// Get dispute detail with full context
    /// </summary>
    Task<DisputeDetailResponse?> GetDisputeDetailAsync(int disputeId);

    /// <summary>
    /// Lấy thông tin bản ghi video (link Drive + trạng thái) của buổi học gắn với tranh chấp.
    /// Dùng cho Admin/Staff khi xử lý tranh chấp.
    /// </summary>
    Task<DisputeRecordingResponse> GetDisputeRecordingAsync(int disputeId);

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

    // ── Tutor-facing (rebuttal channel) ─────────────────────────────────────

    /// <summary>
    /// Get the dispute tied to a tutor's own classSession, if any.
    /// </summary>
    Task<DisputeDetailResponse?> GetTutorDisputeByClassSessionAsync(int classSessionId, string tutorId);

    /// <summary>
    /// Get all disputes across a tutor's own classSessions.
    /// </summary>
    Task<PagedList<DisputeListResponse>> GetTutorDisputesAsync(string tutorId, int page, int pageSize);

    /// <summary>
    /// Tutor submits a written rebuttal to a dispute raised against them.
    /// </summary>
    Task<DisputeDetailResponse> SubmitTutorResponseAsync(int classSessionId, string tutorId, string response);

    /// <summary>
    /// Tutor uploads supporting evidence for a dispute raised against them.
    /// </summary>
    Task<string> UploadTutorDisputeEvidenceAsync(int classSessionId, string tutorId, Microsoft.AspNetCore.Http.IFormFile file);
}
