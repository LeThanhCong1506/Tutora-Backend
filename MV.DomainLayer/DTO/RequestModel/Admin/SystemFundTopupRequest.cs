using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using MV.DomainLayer.Attributes;

namespace MV.DomainLayer.DTO.RequestModel.Admin;

/// <summary>
/// Admin nạp tiền thật vào quỹ hệ thống, kèm ảnh chứng minh — giống mức chặt chẽ của payout
/// thủ công, vì đây cũng là xác nhận tiền thật chứ không phải một con số tự gõ vào.
/// </summary>
public class SystemFundTopupRequest
{
    [Required(ErrorMessage = "Vui lòng nhập số tiền.")]
    [Range(1000, 1_000_000_000, ErrorMessage = "Số tiền phải từ 1.000đ đến 1 tỷ đồng.")]
    public decimal Amount { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập lý do nạp quỹ.")]
    [MinLength(10, ErrorMessage = "Lý do quá ngắn, vui lòng mô tả rõ hơn.")]
    [MaxLength(500, ErrorMessage = "Lý do không được vượt quá 500 ký tự.")]
    public string Reason { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng tải ảnh chứng minh khoản nạp.")]
    [ImageFile]
    public IFormFile ProofImage { get; set; } = null!;
}
