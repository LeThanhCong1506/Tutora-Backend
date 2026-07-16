namespace MV.DomainLayer.Configuration;

/// <summary>
/// Agora RTC settings — AppId và AppCertificate lấy từ Agora Console.
/// Docs: https://docs.agora.io/en/video-calling/get-started/authentication-workflow
/// </summary>
public class AgoraSettings
{
    public const string SectionName = "Agora";

    /// <summary>App ID từ Agora Console.</summary>
    public string AppId { get; set; } = string.Empty;

    /// <summary>App Certificate (Primary Certificate) từ Agora Console — dùng để ký token.</summary>
    public string AppCertificate { get; set; } = string.Empty;

    /// <summary>
    /// Thời gian token có hiệu lực (giây). Mặc định 3600 = 1h.
    /// Agora cho phép tối đa 86400 (24h).
    /// </summary>
    public int TokenExpireSeconds { get; set; } = 3600;
}
