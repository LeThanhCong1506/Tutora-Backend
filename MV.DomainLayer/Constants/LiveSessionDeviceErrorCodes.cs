namespace MV.DomainLayer.Constants;

public static class LiveSessionDeviceErrorCodes
{
    public const string DeviceSessionRequired = "DEVICE_SESSION_REQUIRED";
    public const string InvalidDeviceSession = "INVALID_DEVICE_SESSION";
    public const string ActiveOnAnotherDevice = "SESSION_ACTIVE_ON_ANOTHER_DEVICE";
    public const string LeaseRevoked = "SESSION_LEASE_REVOKED";
}
