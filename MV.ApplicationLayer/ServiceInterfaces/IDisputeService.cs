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
    /// Marks a dispute as investigating. Blocked for the first 48h after creation (so the tutor has
    /// a fair chance to respond first) unless <paramref name="forceEarly"/> is set.
    /// </summary>
    Task<DisputeDetailResponse> InvestigateDisputeAsync(int disputeId, string adminId, bool forceEarly = false);

    /// <summary>
    /// Verify that a no-show report is valid. This unlocks the payer-side remedy picker but does
    /// not move money, settle the class session, warn the tutor, or close the dispute.
    /// </summary>
    Task<DisputeDetailResponse> ConfirmTutorNoShowAsync(int disputeId, string adminId);

    /// <summary>
    /// Resolve dispute with decision
    /// </summary>
    Task<DisputeDetailResponse> ResolveDisputeAsync(int disputeId, string adminId, ResolveDisputeRequest request);

    /// <summary>
    /// Runs AI (Groq) priority classification for a dispute and persists the result. Used both as the
    /// Hangfire job body enqueued right after dispute creation, and as a manual admin re-classify/backfill action.
    /// Returns null if the dispute doesn't exist; leaves Priority/PriorityReason unset (not an error) if the
    /// AI call itself fails or is unavailable.
    /// </summary>
    Task<DisputeDetailResponse?> ClassifyDisputePriorityAsync(int disputeId);

    /// <summary>
    /// Get dispute statistics for admin dashboard
    /// </summary>
    Task<DisputeStatsResponse> GetDisputeStatsAsync();

    /// <summary>
    /// Preview parent refund / tutor payout amounts for a candidate percentage, before resolving
    /// with it. Same clamped math ProcessRefundAsync will actually use — no side effects.
    /// </summary>
    Task<RefundPreviewResponse> GetRefundPreviewAsync(int disputeId, int percentage);

    // ── Parent/Student-facing ────────────────────────────────────────────────

    /// <summary>
    /// Get the dispute tied to a classSession the parent (or self-managed student) owns, if any —
    /// used to view evidence/status of a dispute already created, not just the creation snapshot.
    /// </summary>
    Task<DisputeDetailResponse?> GetDisputeByClassSessionForUserAsync(int classSessionId, string userId, string role);

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

    // ── Dispute chat threads — private per-party channels with admin ───────────

    /// <summary>Admin view of either thread ("tutor" or "parent") for a dispute.</summary>
    Task<List<DisputeMessageResponse>> GetDisputeThreadAsync(int disputeId, string threadType);

    /// <summary>Admin sends a message into one of the two threads.</summary>
    Task<DisputeMessageResponse> SendAdminDisputeMessageAsync(int disputeId, string adminId, string threadType, string message);

    /// <summary>Tutor's own view of their thread for a classSession's dispute.</summary>
    Task<List<DisputeMessageResponse>> GetTutorDisputeThreadAsync(int classSessionId, string tutorId);

    Task<DisputeMessageResponse> SendTutorDisputeMessageAsync(int classSessionId, string tutorId, string message);

    /// <summary>Parent/student's own view of their thread for a classSession's dispute.</summary>
    Task<List<DisputeMessageResponse>> GetPartyDisputeThreadAsync(int classSessionId, string userId, string role);

    Task<DisputeMessageResponse> SendPartyDisputeMessageAsync(int classSessionId, string userId, string role, string message);
}
