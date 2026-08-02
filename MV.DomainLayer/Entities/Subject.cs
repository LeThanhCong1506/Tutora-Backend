using System;
using System.Collections.Generic;

namespace MV.DomainLayer.Entities;

public partial class Subject
{
    public int Subjectid { get; set; }

    public string? Subjectname { get; set; }

    public string? Description { get; set; }

    /// <summary>Soft-delete: true = còn dùng được, false = đã ngừng dùng (ẩn khỏi dropdown).</summary>
    public bool IsActive { get; set; } = true;

    public string? Slug { get; set; }

    public string? IconUrl { get; set; }

    public bool IsHomeworkEnabled { get; set; }

    public int DisplayOrder { get; set; }

    /// <summary>Khối lớp thấp nhất môn này áp dụng (so theo Gradelevel.Levelorder). Null = không giới hạn.</summary>
    public int? MinGradeLevelId { get; set; }

    /// <summary>Khối lớp cao nhất môn này áp dụng (so theo Gradelevel.Levelorder). Null = không giới hạn.</summary>
    public int? MaxGradeLevelId { get; set; }

    public virtual Gradelevel? MinGradeLevel { get; set; }

    public virtual Gradelevel? MaxGradeLevel { get; set; }

    public virtual ICollection<Studentgrade> Studentgrades { get; set; } = new List<Studentgrade>();

    public virtual ICollection<Tutorsubjectgradeprice> Tutorsubjectgradeprices { get; set; } = new List<Tutorsubjectgradeprice>();

    public virtual ICollection<QuestionBank> QuestionBanks { get; set; } = new List<QuestionBank>();
}
