using System.ComponentModel.DataAnnotations;

namespace MV.DomainLayer.DTO.RequestModel.Question;

// Request DTOs cho CRUD danh mục (môn / khối lớp / chương / loại câu hỏi) — CMS admin.

/// <summary>
/// Sắp xếp lại thứ tự hiển thị, gửi nguyên mảng các mục
/// bị đổi vị trí
/// </summary>
public class ReorderRequest
{
    [Required]
    [MinLength(1, ErrorMessage = "Danh sách sắp xếp trống.")]
    public List<ReorderItem> Items { get; set; } = new();
}

public class ReorderItem
{
    [Range(1, int.MaxValue)]
    public int Id { get; set; }

    [Range(0, int.MaxValue)]
    public int DisplayOrder { get; set; }
}

// Subject
public class SubjectRequest
{
    [Required(ErrorMessage = "Vui lòng nhập tên môn học.")]
    [StringLength(150, ErrorMessage = "Tên môn không quá 150 ký tự.")]
    public string SubjectName { get; set; } = null!;

    [StringLength(500)]
    public string? Description { get; set; }

    /// <summary>true = còn dùng được, false = ngừng dùng. Dùng để khôi phục khi Update.</summary>
    public bool IsActive { get; set; } = true;

    [StringLength(150)]
    public string? Slug { get; set; }

    [StringLength(500)]
    public string? IconUrl { get; set; }

    public bool IsHomeworkEnabled { get; set; }

    public int DisplayOrder { get; set; }
}

// GradeLevel
public class GradeLevelRequest
{
    [Required(ErrorMessage = "Vui lòng nhập khối lớp.")]
    [StringLength(100, ErrorMessage = "Tên khối lớp không quá 100 ký tự.")]
    public string GradeName { get; set; } = null!;

    /// <summary>Thứ tự sắp xếp (lớp nhỏ -> lớn).</summary>
    public int LevelOrder { get; set; }

    /// <summary>true = còn dùng được, false = ngừng dùng. Dùng để khôi phục khi Update.</summary>
    public bool IsActive { get; set; } = true;
}

// Chapter
public class ChapterRequest
{
    [Range(1, int.MaxValue, ErrorMessage = "Môn học không hợp lệ.")]
    public int SubjectId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Khối lớp không hợp lệ.")]
    public int GradeLevelId { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập tên chương.")]
    [StringLength(255, ErrorMessage = "Tên chương không quá 255 ký tự.")]
    public string Name { get; set; } = null!;

    /// <summary>Slug định danh; bỏ trống -> tự sinh từ Name.</summary>
    [StringLength(255)]
    public string? Slug { get; set; }

    public int DisplayOrder { get; set; }

    public bool IsActive { get; set; } = true;
}

// QuestionType
public class QuestionTypeRequest
{
    [Required(ErrorMessage = "Vui lòng nhập tên loại câu hỏi.")]
    [StringLength(255, ErrorMessage = "Tên loại không quá 255 ký tự.")]
    public string Name { get; set; } = null!;

    /// <summary>Slug định danh; bỏ trống -> tự sinh từ Name.</summary>
    [StringLength(255)]
    public string? Slug { get; set; }

    public int DisplayOrder { get; set; }

    public bool IsActive { get; set; } = true;
}
