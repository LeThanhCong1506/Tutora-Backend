namespace MV.DomainLayer.Configuration;

public class AgoraNotificationSettings
{
    public const string SectionName = "AgoraNotification";

    /// <summary>Enable HMAC verification after the Agora NCS secret has been configured.</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>Agora Console - Notifications - Secret.</summary>
    public string Secret { get; set; } = string.Empty;
}
