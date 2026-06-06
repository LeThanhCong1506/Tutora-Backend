using System.ComponentModel.DataAnnotations;

namespace MV.DomainLayer.DTO.RequestModel;

/// <summary>
/// Request for tutor to reply to feedback
/// </summary>
public class ReplyFeedbackRequest
{
    /// <summary>
    /// Reply comment from tutor
    /// </summary>
    [Required(ErrorMessage = "Nội dung phản hồi là bắt buộc")]
    [StringLength(1000, MinimumLength = 10, ErrorMessage = "Phản hồi phải từ 10 đến 1000 ký tự")]
    public string ReplyComment { get; set; } = null!;
}
