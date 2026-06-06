namespace MV.DomainLayer.Constants;

/// <summary>
/// Zalo Official Account webhook event type constants. Used by ZaloOAService.
/// </summary>
public static class ZaloOAEventType
{
    public const string Follow       = "follow";
    public const string Unfollow     = "unfollow";
    public const string UserSendText = "user_send_text";
}
