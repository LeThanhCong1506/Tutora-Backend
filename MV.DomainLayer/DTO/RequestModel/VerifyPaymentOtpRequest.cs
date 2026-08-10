using System.ComponentModel.DataAnnotations;

namespace MV.DomainLayer.DTO.RequestModel;

public class VerifyPaymentOtpRequest
{
    [Required]
    [RegularExpression("^(deposit|remaining)$", ErrorMessage = "Phase phải là 'deposit' hoặc 'remaining'.")]
    public string Phase { get; set; } = null!;

    [Required(ErrorMessage = "Vui lòng nhập mã OTP.")]
    [StringLength(6, MinimumLength = 6, ErrorMessage = "Mã OTP phải gồm 6 chữ số.")]
    public string Code { get; set; } = null!;
}
