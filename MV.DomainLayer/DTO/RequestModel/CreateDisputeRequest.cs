using System.ComponentModel.DataAnnotations;

namespace MV.DomainLayer.DTO.RequestModel;

/// <summary>
/// Request for parent to create a dispute for a lesson
/// </summary>
public class CreateDisputeRequest
{
    /// <summary>
    /// Type of dispute: no_show, quality, payment, other
    /// </summary>
    [Required(ErrorMessage = "Loại khiếu nại là bắt buộc")]
    public string DisputeType { get; set; } = null!;

    /// <summary>
    /// Detailed reason for the dispute
    /// </summary>
    [Required(ErrorMessage = "Lý do khiếu nại là bắt buộc")]
    [StringLength(2000, MinimumLength = 10, ErrorMessage = "Lý do phải từ 10 đến 2000 ký tự")]
    public string Reason { get; set; } = null!;

    /// <summary>
    /// Evidence URLs (images, screenshots)
    /// </summary>
    public List<string>? Evidence { get; set; }
}
