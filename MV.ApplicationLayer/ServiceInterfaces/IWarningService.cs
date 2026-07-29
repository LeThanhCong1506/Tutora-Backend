using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;

namespace MV.ApplicationLayer.ServiceInterfaces;

/// <summary>
/// Service interface for warning and suspension management
/// </summary>
public interface IWarningService
{
    /// <summary>
    /// Create a warning for a user
    /// </summary>
    Task<WarningHistoryResponse> CreateWarningAsync(string userId, CreateWarningRequest request, string issuedBy);

    /// <summary>
    /// Get warning history for a user
    /// </summary>
    Task<UserWarningSummaryResponse> GetUserWarningsAsync(string userId);

    /// <summary>
    /// Check and apply suspension if threshold reached
    /// </summary>
    Task<bool> CheckAndApplySuspensionAsync(string userId);

    /// <summary>
    /// Get list of active suspensions
    /// </summary>
    Task<PagedList<SuspensionListResponse>> GetActiveSuspensionsAsync(int page, int pageSize);

    /// <summary>
    /// Get the full suspension history for one user — active and past — newest first.
    /// </summary>
    Task<List<SuspensionListResponse>> GetUserSuspensionsAsync(string userId);

    /// <summary>
    /// Process auto-unsuspend for expired suspensions
    /// Called by background job every hour
    /// </summary>
    Task<int> ProcessAutoUnsuspendAsync(CancellationToken ct = default);

    /// <summary>
    /// Manually unsuspend a user
    /// </summary>
    Task<bool> UnsuspendUserAsync(string userId, string adminId);

    /// <summary>
    /// Create suspension manually (by admin)
    /// </summary>
    Task<SuspensionListResponse> CreateSuspensionAsync(string userId, string suspensionType, string reason, int durationDays, string createdBy);
}
