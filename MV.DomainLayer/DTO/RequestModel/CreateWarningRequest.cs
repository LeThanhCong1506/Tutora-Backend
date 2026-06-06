using System.ComponentModel.DataAnnotations;

namespace MV.DomainLayer.DTO.RequestModel;

/// <summary>
/// Request for admin to create a warning for a user
/// </summary>
public class CreateWarningRequest
{
    /// <summary>
    /// Warning level: 1 = Thấp, 2 = Trung bình, 3 = Cao.
    /// Cao (3) triggers immediate suspension; Thấp/Trung bình trigger suspension after 3 accumulated warnings.
    /// </summary>
    [Required(ErrorMessage = "Mức cảnh báo là bắt buộc")]
    [Range(1, 3, ErrorMessage = "Mức cảnh báo phải là 1 (Thấp), 2 (Trung bình) hoặc 3 (Cao)")]
    public int WarningLevel { get; set; }

    /// <summary>
    /// Reason for the warning
    /// </summary>
    [Required(ErrorMessage = "Lý do cảnh báo là bắt buộc")]
    [StringLength(1000, MinimumLength = 10, ErrorMessage = "Lý do phải từ 10 đến 1000 ký tự")]
    public string Reason { get; set; } = null!;

    /// <summary>
    /// Related booking ID (if applicable)
    /// </summary>
    public int? RelatedBookingId { get; set; }
}
