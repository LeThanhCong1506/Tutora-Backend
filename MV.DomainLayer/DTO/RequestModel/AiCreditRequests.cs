using System.ComponentModel.DataAnnotations;

namespace MV.DomainLayer.DTO.RequestModel;

/// <summary>Client khởi tạo mua gói AI credit.</summary>
public class AiCreditPurchaseRequest
{
    [Required]
    public int PackageId { get; set; }
}

/// <summary>Admin tạo gói AI credit mới.</summary>
public class AiCreditPackageCreateRequest
{
    [Required, MaxLength(30)]
    public string Code { get; set; } = null!;

    [Required, MaxLength(100)]
    public string Name { get; set; } = null!;

    [Range(0, int.MaxValue)]
    public int CreditAmount { get; set; }

    [Range(0, double.MaxValue)]
    public decimal Price { get; set; }

    public string Currency { get; set; } = "VND";

    public bool IsPurchasable { get; set; } = true;

    public bool IsActive { get; set; } = true;

    public int SortOrder { get; set; }

    public string? Description { get; set; }

    public string? IconUrl { get; set; }
}

/// <summary>Admin cập nhật gói AI credit (số lượt Plus/Pro, giá, trạng thái…).</summary>
public class AiCreditPackageUpdateRequest
{
    [Required, MaxLength(100)]
    public string Name { get; set; } = null!;

    [Range(0, int.MaxValue)]
    public int CreditAmount { get; set; }

    [Range(0, double.MaxValue)]
    public decimal Price { get; set; }

    public string Currency { get; set; } = "VND";

    public bool IsPurchasable { get; set; }

    public bool IsActive { get; set; }

    public int SortOrder { get; set; }

    public string? Description { get; set; }

    public string? IconUrl { get; set; }
}
