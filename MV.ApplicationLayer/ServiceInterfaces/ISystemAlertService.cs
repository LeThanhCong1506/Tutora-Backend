using MV.DomainLayer.DTO.ResponseModel.Admin;

namespace MV.ApplicationLayer.ServiceInterfaces;

public interface ISystemAlertService
{
    /// <summary>
    /// Paged list of system alerts, optionally filtered by resolved state.
    /// </summary>
    Task<SystemAlertResponse> GetAlertsAsync(int page, int pageSize, bool? resolved = null, CancellationToken ct = default);

    /// <summary>
    /// Create a system alert with a type, severity, and message.
    /// Returns the new alert's id.
    /// </summary>
    Task<int> CreateAlertAsync(string type, string severity, string message, string? metadata = null, CancellationToken ct = default);

    /// <summary>
    /// Mark an alert as resolved by the given admin user.
    /// </summary>
    Task<bool> ResolveAlertAsync(int alertId, string resolvedBy, CancellationToken ct = default);
}
