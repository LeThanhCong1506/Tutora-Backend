using System;

namespace MV.DomainLayer.Entities;

/// <summary>Bộ đề đánh giá (`assessments`). Admin soạn ở CMS. Không liên quan pool RAG.</summary>
public partial class Assessment
{
    public Guid Id { get; set; }

    public string Title { get; set; } = null!;

    public string? Description { get; set; }

    public int SubjectId { get; set; }

    public int GradeLevelId { get; set; }

    /// <summary>Số câu phải làm. NULL = hết. Nhỏ hơn số đã gán -> rút ngẫu nhiên.</summary>
    public int? QuestionCount { get; set; }

    /// <summary>NULL = không giới hạn thời gian.</summary>
    public int? DurationMinutes { get; set; }

    public bool ShuffleQuestions { get; set; }

    public bool ShuffleOptions { get; set; }

    /// <summary>Cho xem ĐIỂM sau khi nộp. Đáp án thì luôn được xem.</summary>
    public bool ShowResult { get; set; } = true;

    /// <summary>draft | published | archived. Cột DUY NHẤT quyết định đề có phát hay không.</summary>
    public string Status { get; set; } = "draft";

    public string? CreatedBy { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual Subject? Subject { get; set; }

    public virtual Gradelevel? Gradelevel { get; set; }

    public virtual ICollection<AssessmentQuestion> Questions { get; set; } = new List<AssessmentQuestion>();
}
