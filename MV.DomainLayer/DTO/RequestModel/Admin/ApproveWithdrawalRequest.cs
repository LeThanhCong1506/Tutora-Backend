using System.ComponentModel.DataAnnotations;

namespace MV.DomainLayer.DTO.RequestModel.Admin;

public class ApproveWithdrawalRequest
{
    [Required(ErrorMessage = "Vui lòng nhập ghi chú/mã tham chiếu giao dịch chuyển khoản.")]
    [MinLength(3, ErrorMessage = "Ghi chú quá ngắn.")]
    [MaxLength(500)]
    public string Note { get; set; } = string.Empty;
}
