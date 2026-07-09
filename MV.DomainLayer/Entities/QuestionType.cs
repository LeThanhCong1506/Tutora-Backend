using System;

namespace MV.DomainLayer.Entities;

/// <summary>
/// Loại câu hỏi (toàn cục, staff CRUD): Trắc nghiệm, Tự luận, Ghép đôi, Sắp xếp...
/// slug ổn định để query, name để hiển thị.
/// </summary>
public partial class QuestionType
{
    public int Id { get; set; }

    public string Slug { get; set; } = null!;

    public string Name { get; set; } = null!;

    public int DisplayOrder { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual ICollection<QuestionBank> Questions { get; set; } = new List<QuestionBank>();
}
