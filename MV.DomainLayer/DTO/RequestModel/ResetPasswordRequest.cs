using System.ComponentModel.DataAnnotations;

namespace MV.DomainLayer.DTO.RequestModel
{
    /// <summary>Đặt lại mật khẩu bằng OTP gửi tới số điện thoại.</summary>
    public class ResetPasswordRequest
    {
        [Required(ErrorMessage = "Số điện thoại là bắt buộc.")]
        public string Phone { get; set; } = string.Empty;

        [Required(ErrorMessage = "Mã OTP là bắt buộc.")]
        public string Otp { get; set; } = string.Empty;

        [Required(ErrorMessage = "Mật khẩu mới là bắt buộc.")]
        [StringLength(100, MinimumLength = 6, ErrorMessage = "Mật khẩu phải có ít nhất 6 ký tự.")]
        public string NewPassword { get; set; } = string.Empty;
    }
}
