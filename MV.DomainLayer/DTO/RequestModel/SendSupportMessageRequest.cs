using System.ComponentModel.DataAnnotations;

namespace MV.DomainLayer.DTO.RequestModel;

public class SendSupportMessageRequest
{
    [Required(ErrorMessage = "Nội dung tin nhắn là bắt buộc")]
    [StringLength(2000, MinimumLength = 1, ErrorMessage = "Tin nhắn phải từ 1 đến 2000 ký tự")]
    public string Message { get; set; } = null!;
}
