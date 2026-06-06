using System.ComponentModel.DataAnnotations;

namespace MV.DomainLayer.DTO.RequestModel;

public class TutorDecisionRequest
{
    [Required(ErrorMessage = "Lý do từ chối là bắt buộc.")]
    [MinLength(10, ErrorMessage = "Lý do từ chối phải có ít nhất 10 ký tự.")]
    public string Reason { get; set; } = string.Empty;
}
