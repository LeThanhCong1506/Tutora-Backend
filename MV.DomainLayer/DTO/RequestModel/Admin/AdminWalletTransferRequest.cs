using System.ComponentModel.DataAnnotations;

namespace MV.DomainLayer.DTO.RequestModel.Admin;

/// <summary>
/// Admin/staff chủ động cộng tiền vào ví một user. Không có trường ngân hàng — tiền vào
/// thẳng ví trong app, không phải một khoản chuyển khoản ra ngoài hệ thống.
/// </summary>
public class AdminWalletTransferRequest
{
    [Required(ErrorMessage = "Vui lòng chọn người nhận.")]
    public string RecipientUserId { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập số tiền.")]
    [Range(1000, 1_000_000_000, ErrorMessage = "Số tiền phải từ 1.000đ đến 1 tỷ đồng.")]
    public decimal Amount { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập lý do chuyển tiền.")]
    [MinLength(10, ErrorMessage = "Lý do quá ngắn, vui lòng mô tả rõ hơn.")]
    [MaxLength(500, ErrorMessage = "Lý do không được vượt quá 500 ký tự.")]
    public string Reason { get; set; } = string.Empty;
}
