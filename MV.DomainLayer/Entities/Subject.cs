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

    public virtual ICollection<Studentgrade> Studentgrades { get; set; } = new List<Studentgrade>();

    public virtual ICollection<Tutorsubjectgradeprice> Tutorsubjectgradeprices { get; set; } = new List<Tutorsubjectgradeprice>();

    public virtual ICollection<QuestionBank> QuestionBanks { get; set; } = new List<QuestionBank>();
}
