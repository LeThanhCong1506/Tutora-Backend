using System;

namespace MV.DomainLayer.Entities;

/// <summary>
/// Chương học — gắn với (môn, lớp). slug để query ổn định, name để hiển thị.
/// vd slug="ung_dung_dao_ham", name="Ứng dụng đạo hàm", subject=Toán, grade=Lớp 12.
/// </summary>
public partial class Chapter
{
    public int Id { get; set; }

    public int SubjectId { get; set; }

    public int GradeLevelId { get; set; }

    public string Slug { get; set; } = null!;

    public string Name { get; set; } = null!;

    public int DisplayOrder { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual Subject? Subject { get; set; }

    public virtual Gradelevel? Gradelevel { get; set; }

    public virtual ICollection<QuestionBank> Questions { get; set; } = new List<QuestionBank>();
}
