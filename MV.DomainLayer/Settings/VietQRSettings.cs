namespace MV.DomainLayer.Settings;

public class VietQRSettings
{
    public const string SectionName = "VietQR";
    public const string DefaultBaseUrl = "https://api.vietqr.io";

    public string BaseUrl { get; set; } = DefaultBaseUrl;
    public int TimeoutSeconds { get; set; } = 30;

    // Cache configuration
    public int L1CacheMinutes { get; set; } = 10;
    public int L2CacheHours { get; set; } = 24;
    public int BackgroundRefreshMinutes { get; set; } = 8;
    public bool EnableStaleWhileRevalidate { get; set; } = true;
}
