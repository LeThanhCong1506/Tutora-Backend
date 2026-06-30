using System.ComponentModel.DataAnnotations;

namespace MV.DomainLayer.DTO.RequestModel
{
    /// <summary>Xác thực số điện thoại bằng mã OTP (sau khi đăng ký).</summary>
    public class VerifyPhoneOtpRequest
    {
        [Required(ErrorMessage = "Số điện thoại là bắt buộc.")]
        public string Phone { get; set; } = string.Empty;

        [Required(ErrorMessage = "Mã OTP là bắt buộc.")]
        public string Otp { get; set; } = string.Empty;
    }
}
