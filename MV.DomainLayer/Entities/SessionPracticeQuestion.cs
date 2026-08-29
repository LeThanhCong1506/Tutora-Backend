using System;

namespace MV.DomainLayer.Entities;

/// <summary>
/// Một câu hỏi trong bộ bài tập buổi học (`practice_questions`).
///
/// KHÔNG BAO GIỜ embed vào pool RAG: nếu embed thì buổi sau học sinh chụp chính câu
/// này gửi /solve là AI đọc luôn lời giải — bài kiểm tra tự phá chính nó.
/// </summary>
public partial class SessionPracticeQuestion
{
    public Guid Id { get; set; }

    public Guid SetId { get; set; }

    /// <summary>Vị trí câu trong bộ (1-based).</summary>
    public int DisplayOrder { get; set; }

    /// <summary>mc (trắc nghiệm, chấm tự động) | essay (tự luận, gia sư xem miệng).</summary>
    public string QuestionFormat { get; set; } = null!;

    /// <summary>Đề bài. LaTeX inline kẹp trong $...$ — FE render bằng KaTeX.</summary>
    public string Content { get; set; } = null!;

    /// <summary>Phương án A/B/C/D. Null với câu tự luận.</summary>
    public List<AnswerOption>? AnswerOptions { get; set; }

    /// <summary>Chỉ mc: đúng 1 key trong AnswerOptions. Tự luận để null.</summary>
    public string? CorrectAnswer { get; set; }

    /// <summary>Giải thích ngắn, hiện cho học sinh SAU khi chọn đáp án.</summary>
    public string? Explanation { get; set; }

    /// <summary>Tài liệu AI lấy ý ra câu này — để hiện "Trích từ ... trang N".</summary>
    public int? SourceMaterialId { get; set; }

    public int? SourcePage { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual SessionPracticeSet? Set { get; set; }

    public virtual Learningmaterial? SourceMaterial { get; set; }

    public virtual ICollection<SessionPracticeAnswer> Answers { get; set; } = new List<SessionPracticeAnswer>();
}
