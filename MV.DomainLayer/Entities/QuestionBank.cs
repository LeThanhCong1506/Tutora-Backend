using System;

namespace MV.DomainLayer.Entities;

/// <summary>Nguồn kiến thức/câu hỏi dùng cho RAG khi AI giải bài tập.</summary>
public partial class QuestionBank
{
    public int Questionid { get; set; }

    public int? Subjectid { get; set; }

    public int? Gradelevelid { get; set; }

    public string Content { get; set; } = null!;

    public string? Answer { get; set; }

    public string? Metadata { get; set; }

    public string? Embedding { get; set; }

    public bool Isactive { get; set; } = true;

    public DateTime? Createdat { get; set; }

    public DateTime? Updatedat { get; set; }

    public virtual Subject? Subject { get; set; }

    public virtual Gradelevel? Gradelevel { get; set; }
}
