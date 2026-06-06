namespace MV.DomainLayer.DTO.RequestModel;

/// <summary>
/// Request model để đăng nhập bằng Zalo Mini App
/// </summary>
public class ZaloLoginRequest
{
    public string ZaloAccessToken { get; set; } = string.Empty;
    public string ZaloUserId { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? Avatar { get; set; }
    public string? Role { get; set; }
}
