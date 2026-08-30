using System;

namespace MV.DomainLayer.Entities;

/// <summary>
/// Bài làm của học sinh cho 1 câu (`practice_answers`).
///
/// 1 dòng / (câu, học sinh) — làm lại thì GHI ĐÈ. KHÔNG có cột điểm/nhận xét: gia sư
/// chấm bằng miệng ngay trong buổi, trắc nghiệm thì đối chiếu CorrectAnswer là ra.
/// </summary>
public partial class SessionPracticeAnswer
{
    public Guid Id { get; set; }

    public Guid QuestionId { get; set; }

    public string StudentId { get; set; } = null!;

    /// <summary>mc: key đã chọn ("A"). essay: nguyên văn bài làm.</summary>
    public string Answer { get; set; } = null!;

    /// <summary>Chỉ mc mới có đúng/sai. Tự luận luôn null.</summary>
    public bool? IsCorrect { get; set; }

    public DateTime AnsweredAt { get; set; }

    public virtual SessionPracticeQuestion? Question { get; set; }
}
