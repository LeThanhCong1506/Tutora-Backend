using MV.DomainLayer.DTO.ResponseModel.Admin;

namespace MV.ApplicationLayer.ServiceInterfaces;

public interface IAdminDashboardService
{
    /// <summary>
    /// Returns a lightweight platform overview snapshot for the admin dashboard home.
    /// No filter params — always reflects current state.
    /// </summary>
    Task<AdminDashboardStatsResponse> GetStatsAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns user growth and distribution statistics.
    /// Supports optional date-range filtering for growth metrics (default: last 30 days).
    /// </summary>
    Task<AdminUserStatsResponse> GetUserStatsAsync(
        DateTime? from,
        DateTime? to,
        CancellationToken ct = default);

    /// <summary>
    /// Returns tutor performance metrics including top tutors by rating, lessons, and revenue.
    /// Supports optional date-range filtering and result-count cap.
    /// </summary>
    Task<AdminTutorPerformanceResponse> GetTutorPerformanceAsync(
        int top,
        DateTime? from,
        DateTime? to,
        CancellationToken ct = default);

    /// <summary>
    /// Returns dispute statistics including overview counts, financial impact, type breakdown, and monthly trend.
    /// Supports optional date-range filtering (default: last 30 days).
    /// </summary>
    Task<AdminDisputeStatsResponse> GetDisputeStatsAsync(
        DateTime? from,
        DateTime? to,
        CancellationToken ct = default);
}
