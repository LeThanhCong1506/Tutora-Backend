namespace MV.DomainLayer.DTO.ResponseModel.Policy;

/// <summary>Một mục trong danh sách văn bản — không kèm nội dung để danh sách nhẹ.</summary>
public class PolicyDocumentSummaryResponse
{
    public int PolicyDocumentId { get; set; }
    public string Slug { get; set; } = null!;
    public string Title { get; set; } = null!;
    public string? Summary { get; set; }
    public string Version { get; set; } = null!;
    public DateOnly? EffectiveDate { get; set; }
    public string Status { get; set; } = null!;
    public int DisplayOrder { get; set; }
    public DateTime? PublishedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    /// <summary>Tên người sửa gần nhất — chỉ trả cho CMS, trang công khai không cần.</summary>
    public string? UpdatedByName { get; set; }
}

/// <summary>Chi tiết văn bản, kèm nội dung Markdown để FE render.</summary>
public class PolicyDocumentResponse : PolicyDocumentSummaryResponse
{
    public string ContentMarkdown { get; set; } = null!;
}
