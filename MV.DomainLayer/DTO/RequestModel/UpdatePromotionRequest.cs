using System.ComponentModel.DataAnnotations;

namespace MV.DomainLayer.DTO.RequestModel;

/// <summary>
/// Admin update promotion — tất cả field đều nullable, chỉ field nào có giá trị mới được cập nhật.
/// </summary>
public class UpdatePromotionRequest
{
    [MaxLength(500)]
    public string? Description { get; set; }

    [RegularExpression(@"^(percent|fixed)$", ErrorMessage = "DiscountType phải là 'percent' hoặc 'fixed'.")]
    public string? DiscountType { get; set; }

    [Range(0, double.MaxValue, ErrorMessage = "DiscountValue phải >= 0.")]
    public decimal? DiscountValue { get; set; }

    [Range(0, double.MaxValue, ErrorMessage = "MaxDiscountAmount phải >= 0.")]
    public decimal? MaxDiscountAmount { get; set; }

    [Range(0, double.MaxValue, ErrorMessage = "MinOrderValue phải >= 0.")]
    public decimal? MinOrderValue { get; set; }

    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }

    [Range(0, int.MaxValue, ErrorMessage = "UsageLimit phải >= 0.")]
    public int? UsageLimit { get; set; }

    public bool? IsActive { get; set; }
}
