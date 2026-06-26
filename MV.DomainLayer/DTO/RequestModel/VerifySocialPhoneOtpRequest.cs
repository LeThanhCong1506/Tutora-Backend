using System.ComponentModel.DataAnnotations;

namespace MV.DomainLayer.DTO.RequestModel;

public class VerifySocialPhoneOtpRequest
{
    [Required(ErrorMessage = "Social registration token là bắt buộc.")]
    [StringLength(128, MinimumLength = 32, ErrorMessage = "Social registration token không hợp lệ.")]
    public string SocialRegistrationToken { get; set; } = string.Empty;

    [Required(ErrorMessage = "Mã OTP là bắt buộc.")]
    [RegularExpression(@"^\d{6}$", ErrorMessage = "OTP phải gồm đúng 6 chữ số.")]
    public string Otp { get; set; } = string.Empty;
}
