namespace MV.DomainLayer.Configuration;

/// <summary>
/// Agora Interactive Whiteboard (Netless) settings — lấy từ Agora Console (Whiteboard).
/// Docs: https://docs.agora.io/en/interactive-whiteboard/get-started/get-started-sdk
/// </summary>
public class WhiteboardSettings
{
    public const string SectionName = "Whiteboard";

    /// <summary>App Identifier của dự án Whiteboard (dùng ở client để khởi tạo SDK).</summary>
    public string AppIdentifier { get; set; } = string.Empty;

    /// <summary>Access Key (AK) — dùng để ký SDK/Room token.</summary>
    public string AccessKey { get; set; } = string.Empty;

    /// <summary>Secret Key (SK) — dùng để ký SDK/Room token.</summary>
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>Region của dữ liệu whiteboard (vd "sg", "cn-hz", "us-sw", "in-mum", "gb-lon").</summary>
    public string Region { get; set; } = "sg";
}
