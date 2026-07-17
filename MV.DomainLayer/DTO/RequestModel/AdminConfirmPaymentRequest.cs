using System.ComponentModel.DataAnnotations;

namespace MV.DomainLayer.DTO.RequestModel;

public class AdminConfirmPaymentRequest
{
    [Range(typeof(decimal), "1", "2147483647", ErrorMessage = "Số tiền thanh toán phải lớn hơn 0.")]
    public decimal Amount { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập mã giao dịch ngân hàng.")]
    [MaxLength(255, ErrorMessage = "Mã giao dịch không được vượt quá 255 ký tự.")]
    public string TransactionId { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập thời gian giao dịch.")]
    public DateTimeOffset? PaidAt { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập ghi chú đối soát.")]
    [MinLength(3, ErrorMessage = "Ghi chú quá ngắn.")]
    [MaxLength(500, ErrorMessage = "Ghi chú không được vượt quá 500 ký tự.")]
    public string Note { get; set; } = string.Empty;
}
