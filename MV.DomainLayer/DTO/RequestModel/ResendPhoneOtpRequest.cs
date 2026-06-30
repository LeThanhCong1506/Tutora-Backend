using System.ComponentModel.DataAnnotations;

namespace MV.DomainLayer.DTO.RequestModel
{
    /// <summary>Gửi lại mã OTP xác thực số điện thoại.</summary>
    public class ResendPhoneOtpRequest
    {
        [Required(ErrorMessage = "Số điện thoại là bắt buộc.")]
        public string Phone { get; set; } = string.Empty;
    }
}
