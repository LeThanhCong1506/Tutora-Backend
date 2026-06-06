using System.ComponentModel.DataAnnotations;

namespace MV.DomainLayer.DTO.RequestModel;

public class CreatePromotionRequest
{
    [Required]
    [MaxLength(50)]
    public string Code { get; set; } = null!;

    public string? Description { get; set; }

    [RegularExpression(@"^(percent|fixed)$", ErrorMessage = "DiscountType must be percent or fixed")]
    public string? DiscountType { get; set; }

    [Range(0, double.MaxValue)]
    public decimal? DiscountValue { get; set; }

    [Range(0, double.MaxValue)]
    public decimal? MaxDiscountAmount { get; set; }

    [Range(0, double.MaxValue)]
    public decimal? MinOrderValue { get; set; }

    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }

    [Range(0, int.MaxValue)]
    public int? UsageLimit { get; set; }

    public bool IsActive { get; set; } = true;
}
