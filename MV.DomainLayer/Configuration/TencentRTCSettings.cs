namespace MV.DomainLayer.Configuration;

/// <summary>
/// Tencent RTC (TRTC) settings — SdkAppId và SecretKey lấy từ Tencent RTC Console.
/// Docs: https://trtc.io/document/35166
/// </summary>
public class TencentRTCSettings
{
    public const string SectionName = "TencentRTC";

    /// <summary>SDKAppID từ Tencent RTC Console (e.g. 20042637)</summary>
    public int SdkAppId { get; set; }

    /// <summary>SecretKey từ Tencent RTC Console</summary>
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>Thời gian UserSig có hiệu lực (giây). Mặc định 86400 = 24h.</summary>
    public int TokenExpireSeconds { get; set; } = 86400;
}
