namespace MV.DomainLayer.DTO.ResponseModel.Zalo;

/// <summary>
/// Kết quả gửi tin nhắn Zalo OA / ZNS
/// </summary>
public class ZaloSendResult
{
    public bool Success { get; set; }
    public string? MessageId { get; set; }
    public string? Error { get; set; }
    public bool FallbackUsed { get; set; }
}

/// <summary>
/// Nút quick reply cho Zalo OA message
/// </summary>
public class ZaloQuickReply
{
    public string Title { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public string? ImageIcon { get; set; }
    public string Type { get; set; } = "oa.query.show";
}
