using System;

namespace MV.DomainLayer.Entities;

/// <summary>
/// Câu hỏi của 1 bộ đề (`assessment_questions`). TÁCH khỏi <see cref="QuestionBank"/> có
/// chủ đích: gộp vào pool RAG thì AI sẽ trích nguyên đề + lộ đáp án. Không bao giờ embed.
/// </summary>
public partial class AssessmentQuestion
{
    public Guid Id { get; set; }

    public Guid AssessmentId { get; set; }

    /// <summary>Vị trí câu trong đề (1-based).</summary>
    public int DisplayOrder { get; set; }

    /// <summary>Điểm câu này.</summary>
    public decimal Points { get; set; } = 1;

    /// <summary>Cách chấm, suy từ slug của <see cref="QuestionTypeId"/> khi lưu.</summary>
    public string QuestionFormat { get; set; } = null!;

    /// <summary>FK chapters.</summary>
    public int? ChapterId { get; set; }

    /// <summary>FK question_types — admin chọn; slug quyết định cách chấm.</summary>
    public int? QuestionTypeId { get; set; }

    /// <summary>NHAN_BIET | THONG_HIEU | VAN_DUNG | VAN_DUNG_CAO.</summary>
    public string? Difficulty { get; set; }

    public string Content { get; set; } = null!;

    /// <summary>Phương án A/B/C/D. Null với loại nhập tay.</summary>
    public List<AnswerOption>? AnswerOptions { get; set; }

    /// <summary>CSV key đúng ("A" / "A,C"); loại nhập tay là chuỗi đáp án.</summary>
    public string CorrectAnswer { get; set; } = null!;

    /// <summary>Chỉ loại nhập tay: cách viết khác cũng đúng, vd ["0.5","1/2"].</summary>
    public List<string>? AcceptedAnswers { get; set; }

    public string? Explanation { get; set; }

    /// <summary>Ảnh kèm câu (Cloudinary).</summary>
    public List<string> ImageUrls { get; set; } = new();

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual Assessment? Assessment { get; set; }

    public virtual Chapter? ChapterNav { get; set; }

    public virtual QuestionType? QuestionType { get; set; }
}
