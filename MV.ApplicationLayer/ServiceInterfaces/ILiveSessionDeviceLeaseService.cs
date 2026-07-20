namespace MV.ApplicationLayer.ServiceInterfaces;

/// <summary>
/// Coordinates the single active device lease for one authenticated user in one live class session.
/// Implementations must make admission and takeover atomic across all application instances.
/// </summary>
public interface ILiveSessionDeviceLeaseService
{
    Task<LiveSessionAdmissionResult> AdmitAsync(
        int classSessionId,
        string userId,
        string participationId,
        string deviceId,
        string deviceLabel,
        CancellationToken cancellationToken = default);

    Task<LiveSessionTakeoverResult> TakeoverAsync(
        int classSessionId,
        string userId,
        string participationId,
        string deviceId,
        string deviceLabel,
        string expectedActiveLeaseId,
        CancellationToken cancellationToken = default);

    Task<bool> RenewAsync(
        int classSessionId,
        string userId,
        string participationId,
        string leaseId,
        CancellationToken cancellationToken = default);

    Task<bool> IsActiveAsync(
        int classSessionId,
        string userId,
        string participationId,
        string leaseId,
        CancellationToken cancellationToken = default);

    Task<bool> ReleaseAsync(
        int classSessionId,
        string userId,
        string participationId,
        string leaseId,
        CancellationToken cancellationToken = default);
}

public sealed record LiveSessionDeviceLease(
    string ParticipationId,
    string LeaseId,
    string DeviceId,
    string DeviceLabel);

public sealed record LiveSessionAdmissionResult(
    bool Admitted,
    LiveSessionDeviceLease? Lease,
    string? ActiveLeaseId,
    string? ActiveDeviceLabel);

public sealed record LiveSessionTakeoverResult(
    bool TakenOver,
    LiveSessionDeviceLease? Lease,
    string? ReplacedLeaseId,
    string? ActiveLeaseId,
    string? ActiveDeviceLabel);
