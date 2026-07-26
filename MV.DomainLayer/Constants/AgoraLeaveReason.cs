namespace MV.DomainLayer.Constants;

/// <summary>
/// Why a user left an Agora channel (payload.reason on event 108).
/// Telling "the network dropped" apart from "they walked out" is the whole point of keeping this
/// log, so the labels are written for staff reading a dispute, not for developers.
/// </summary>
public static class AgoraLeaveReason
{
    public const int Other = 0;
    public const int Quit = 1;
    public const int ConnectionTimeout = 2;
    public const int PermissionDenied = 3;
    public const int LoadBalancing = 4;
    public const int NewDeviceLogin = 5;
    public const int MultipleIpReconnect = 9;
    public const int NetworkReconnect = 10;
    public const int TokenError = 12;
    public const int UnknownNetwork = 99;
    public const int UnusualActivity = 999;

    /// <summary>True when the participant did not choose to leave — the connection failed on them.</summary>
    public static bool IsInvoluntary(int? reason) => reason is
        ConnectionTimeout or LoadBalancing or MultipleIpReconnect or NetworkReconnect or UnknownNetwork;

    public static string Label(int? reason) => reason switch
    {
        null => "Không rõ",
        Quit => "Chủ động rời phòng",
        ConnectionTimeout => "Mất kết nối (quá 10 giây không có dữ liệu)",
        PermissionDenied => "Bị buộc rời phòng",
        LoadBalancing => "Máy chủ Agora ngắt để cân bằng tải",
        NewDeviceLogin => "Bị đẩy ra do đăng nhập ở thiết bị khác",
        MultipleIpReconnect => "Đổi địa chỉ IP, đang kết nối lại",
        NetworkReconnect => "Sự cố mạng, đang kết nối lại",
        TokenError => "Token lỗi hoặc hết hạn (lỗi hệ thống)",
        UnknownNetwork => "Sự cố mạng không xác định",
        UnusualActivity => "Hoạt động bất thường (vào/ra liên tục)",
        Other => "Khác",
        _ => $"Mã {reason}"
    };
}

/// <summary>Client platform reported by Agora (payload.platform).</summary>
public static class AgoraPlatform
{
    public static string Label(int? platform) => platform switch
    {
        null => "Không rõ",
        1 => "Android",
        2 => "iOS",
        5 => "Windows",
        6 => "Linux",
        7 => "Web",
        8 => "macOS",
        0 => "Khác",
        _ => $"Mã {platform}"
    };
}
