using System.ComponentModel.DataAnnotations;

namespace MV.DomainLayer.DTO.RequestModel;

/// <summary>
/// Yêu cầu gửi OTP xác thực giao dịch lớn cho một booking. <see cref="Phase"/> phải khớp với
/// khoản đang được trả ngay lúc gọi ("deposit" cho cọc buổi đầu, "remaining" cho phần còn lại) —
/// FE luôn biết chính xác giá trị này vì nó lấy từ tóm tắt thanh toán đang hiển thị.
/// </summary>
public class SendPaymentOtpRequest
{
    [Required]
    [RegularExpression("^(deposit|remaining)$", ErrorMessage = "Phase phải là 'deposit' hoặc 'remaining'.")]
    public string Phase { get; set; } = null!;
}
