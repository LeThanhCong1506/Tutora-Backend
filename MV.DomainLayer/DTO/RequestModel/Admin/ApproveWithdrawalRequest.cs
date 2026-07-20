using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using MV.DomainLayer.Attributes;

namespace MV.DomainLayer.DTO.RequestModel.Admin;

public class ApproveWithdrawalRequest
{
    [Required(ErrorMessage = "Vui lòng nhập thời gian giao dịch.")]
    public DateTimeOffset? PaidAt { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập ghi chú đối soát giao dịch chuyển khoản.")]
    [MinLength(3, ErrorMessage = "Ghi chú quá ngắn.")]
    [MaxLength(500, ErrorMessage = "Ghi chú không được vượt quá 500 ký tự.")]
    public string Note { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng tải ảnh biên lai chuyển khoản.")]
    [ImageFile]
    public IFormFile ProofImage { get; set; } = null!;
}
