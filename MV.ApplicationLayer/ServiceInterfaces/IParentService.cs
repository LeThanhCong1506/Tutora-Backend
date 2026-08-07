using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;

namespace MV.ApplicationLayer.ServiceInterfaces;

/// <summary>
/// Service interface for parent classSession management
/// </summary>
public interface IParentService
{
    /// <summary>
    /// Get classSessions pending parent/student confirmation
    /// </summary>
    Task<List<PendingClassSessionResponse>> GetPendingClassSessionsAsync(string userId, string role);

    /// <summary>
    /// Get classSession detail for parent/student view
    /// </summary>
    Task<ClassSessionDetailResponse?> GetClassSessionDetailAsync(int classSessionId, string userId, string role);

    /// <summary>
    /// Confirm a classSession as completed (triggers settlement)
    /// </summary>
    Task<SettlementResultResponse> ConfirmClassSessionAsync(int classSessionId, string userId, string role);

    /// <summary>
    /// Create a dispute for a classSession
    /// </summary>
    Task<DisputeDetailResponse> CreateDisputeAsync(int classSessionId, string userId, string role, CreateDisputeRequest request);

    /// <summary>
    /// Get parent's/student's dispute history
    /// </summary>
    Task<PagedList<DisputeListResponse>> GetParentDisputesAsync(string userId, PortalDisputeQueryRequest query);

    /// <summary>
    /// Get calendar view
    /// </summary>
    Task<List<CalendarDayResponse>> GetParentCalendarAsync(string userId, string role, DateTime startDate, DateTime endDate);

}

