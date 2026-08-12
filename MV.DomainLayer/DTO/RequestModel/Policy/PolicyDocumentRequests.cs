using System.ComponentModel.DataAnnotations;

namespace MV.DomainLayer.DTO.RequestModel.Policy;

public class CreatePolicyDocumentRequest
{
    /// <summary>Chỉ chữ thường, số và dấu gạch ngang — slug đi thẳng vào URL công khai.</summary>
    [Required(ErrorMessage = "Slug là bắt buộc.")]
    [MaxLength(80)]
    [RegularExpression("^[a-z0-9]+(-[a-z0-9]+)*$", ErrorMessage = "Slug chỉ gồm chữ thường, số và dấu gạch ngang.")]
    public string Slug { get; set; } = null!;

    [Required(ErrorMessage = "Tiêu đề là bắt buộc.")]
    [MaxLength(200)]
    public string Title { get; set; } = null!;

    [MaxLength(500)]
    public string? Summary { get; set; }

    [Required(ErrorMessage = "Nội dung là bắt buộc.")]
    public string ContentMarkdown { get; set; } = null!;

    [MaxLength(20)]
    public string? Version { get; set; }

    public DateOnly? EffectiveDate { get; set; }

    public int? DisplayOrder { get; set; }
}

/// <summary>Slug không nằm trong đây: đổi slug làm chết mọi liên kết đã phát hành.</summary>
public class UpdatePolicyDocumentRequest
{
    [Required(ErrorMessage = "Tiêu đề là bắt buộc.")]
    [MaxLength(200)]
    public string Title { get; set; } = null!;

    [MaxLength(500)]
    public string? Summary { get; set; }

    [Required(ErrorMessage = "Nội dung là bắt buộc.")]
    public string ContentMarkdown { get; set; } = null!;

    [MaxLength(20)]
    public string? Version { get; set; }

    public DateOnly? EffectiveDate { get; set; }

    public int? DisplayOrder { get; set; }
}
