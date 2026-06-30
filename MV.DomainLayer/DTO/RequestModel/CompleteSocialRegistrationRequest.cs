using System.ComponentModel.DataAnnotations;

namespace MV.DomainLayer.DTO.RequestModel;

public class CompleteSocialRegistrationRequest
{
    [Required(ErrorMessage = "Social registration token là bắt buộc.")]
    [StringLength(128, MinimumLength = 32, ErrorMessage = "Social registration token không hợp lệ.")]
    public string SocialRegistrationToken { get; set; } = string.Empty;

    public string? Role { get; set; }

    [Required(ErrorMessage = "Số điện thoại là bắt buộc.")]
    [RegularExpression(@"^\+?\d{9,15}$", ErrorMessage = "Số điện thoại phải gồm 9-15 chữ số và có thể bắt đầu bằng dấu +.")]
    public string Phone { get; set; } = string.Empty;
}
