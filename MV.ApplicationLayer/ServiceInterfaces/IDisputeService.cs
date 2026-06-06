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
