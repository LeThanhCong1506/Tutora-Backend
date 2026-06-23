using System.ComponentModel.DataAnnotations;

namespace MV.DomainLayer.DTO.RequestModel;

/// <summary>
/// Request model để đăng nhập bằng Zalo Login v4 (Web OAuth).
/// FE redirect sang Zalo, nhận authorization code, gửi code + codeVerifier (PKCE) lên backend.
/// </summary>
public class ZaloWebLoginRequest
{
    /// <summary>Authorization code Zalo trả về ở redirect URI (dùng 1 lần).</summary>
    [Required(ErrorMessage = "Authorization code là bắt buộc.")]
    public string Code { get; set; } = string.Empty;

    /// <summary>PKCE code_verifier mà FE đã tạo trước khi redirect.</summary>
    [Required(ErrorMessage = "Code verifier là bắt buộc.")]
    public string CodeVerifier { get; set; } = string.Empty;

    /// <summary>
    /// Redirect URI FE đã dùng khi authorize. Zalo Login v4 KHÔNG yêu cầu redirect_uri ở
    /// bước đổi token (PKCE thay thế việc verify này), nên backend chỉ nhận để tham khảo/log.
    /// </summary>
    public string? RedirectUri { get; set; }

    /// <summary>Vai trò khi đăng ký mới (Parent/Student/Tutor). Không cần khi đã có tài khoản.</summary>
    public string? Role { get; set; }
}
