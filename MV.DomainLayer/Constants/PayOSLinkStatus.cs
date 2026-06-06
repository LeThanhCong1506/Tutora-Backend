namespace MV.DomainLayer.Constants;

/// <summary>
/// PayOS payment link status constants. Values are UPPERCASE as returned by the PayOS SDK.
/// </summary>
public static class PayOSLinkStatus
{
    public const string Pending    = "PENDING";
    public const string Processing = "PROCESSING";
    public const string Paid       = "PAID";
    public const string Cancelled  = "CANCELLED";
    public const string Expired    = "EXPIRED";
}
