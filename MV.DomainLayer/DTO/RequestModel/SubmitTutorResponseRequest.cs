using System.ComponentModel.DataAnnotations;

namespace MV.DomainLayer.DTO.RequestModel;

/// <summary>
/// Tutor's rebuttal to a dispute raised against them.
/// </summary>
public class SubmitTutorResponseRequest
{
    [Required(ErrorMessage = "Nội dung phản hồi là bắt buộc")]
    [StringLength(2000, MinimumLength = 10, ErrorMessage = "Phản hồi phải từ 10 đến 2000 ký tự")]
    public string Response { get; set; } = null!;
}
