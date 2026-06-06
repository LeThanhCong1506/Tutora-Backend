namespace MV.DomainLayer.DTO.ResponseModel;

/// <summary>
/// Full promotion detail — used for admin list and detail views.
/// </summary>
public class PromotionResponse
{
    public int PromotionId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>"percent" | "fixed"</summary>
    public string? DiscountType { get; set; }
    public decimal? DiscountValue { get; set; }
    public decimal? MaxDiscountAmount { get; set; }
    public decimal? MinOrderValue { get; set; }

    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }

    public int? UsageLimit { get; set; }
    public int? UsageCount { get; set; }

    /// <summary>true = đang hoạt động, false = đã tắt</summary>
    public bool? IsActive { get; set; }

    /// <summary>Trạng thái tính theo thời gian thực: Active | Upcoming | Expired | Inactive</summary>
    public string RuntimeStatus { get; set; } = string.Empty;

    public DateTime? CreatedAt { get; set; }
}

public class PromotionListResponse
{
    public List<PromotionResponse> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}
