namespace MV.DomainLayer.Configuration;

/// <summary>
/// Agora RTC settings — AppId và AppCertificate lấy từ Agora Console (console.agora.io).
/// </summary>
public class AgoraSettings
{
    public const string SectionName = "Agora";

    /// <summary>App ID từ Agora Console.</summary>
    public string AppId { get; set; } = string.Empty;

    /// <summary>App Certificate từ Agora Console (dùng để ký token).</summary>
    public string AppCertificate { get; set; } = string.Empty;

    /// <summary>Thời gian token có hiệu lực (giây). Mặc định 86400 = 24h.</summary>
    public int TokenExpireSeconds { get; set; } = 86400;
}
