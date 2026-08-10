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
    /// Returns null if the dispute doesn't exist. <paramref name="actorId"/> scopes the returned response's
    /// recording stream token — pass "system" from the background trigger, where the response is discarded.
    /// </summary>
    /// <param name="swallowClassificationFailure">
    /// True for the Hangfire background trigger: leaves Priority/PriorityReason unset (not an error) if the AI
    /// call fails, so the job completes successfully instead of being retried and eventually marked permanently
    /// Failed by Hangfire (which would leave the dispute unclassified forever with no automatic retry). False
    /// (default) for the manual admin action, where the caller needs the exception to report a real failure.
    /// </param>
    Task<DisputeDetailResponse?> ClassifyDisputePriorityAsync(int disputeId, string actorId, bool swallowClassificationFailure = false);

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
    /// Get all disputes across a tutor's own classSessions, with filtering, sorting, and pagination.
    /// </summary>
    Task<PagedList<DisputeListResponse>> GetTutorDisputesAsync(string tutorId, PortalDisputeQueryRequest query);

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
