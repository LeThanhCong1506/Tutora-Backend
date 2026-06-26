using System.ComponentModel.DataAnnotations;

namespace MV.DomainLayer.DTO.RequestModel;

public class ResendSocialPhoneOtpRequest
{
    [Required(ErrorMessage = "Social registration token là bắt buộc.")]
    [StringLength(128, MinimumLength = 32, ErrorMessage = "Social registration token không hợp lệ.")]
    public string SocialRegistrationToken { get; set; } = string.Empty;
}
