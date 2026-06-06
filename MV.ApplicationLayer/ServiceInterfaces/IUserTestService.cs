namespace MV.ApplicationLayer.ServiceInterfaces;

/// <summary>
/// Test-only helpers — NOT exposed in production endpoints.
/// </summary>
public interface IUserTestService
{
    /// <summary>
    /// Hard-delete a user and all related records; used in automated test teardown.
    /// </summary>
    Task<(bool Success, string Message)> DeleteUserForTestAsync(string userId);

    /// <summary>
    /// Repair a booking that is stuck in an inconsistent state; used in test environments.
    /// </summary>
    Task<(bool Found, string Message)> FixBookingAsync(int bookingId);
}
