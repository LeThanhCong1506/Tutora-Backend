using System;

namespace MV.DomainLayer.Entities;

/// <summary>
/// Trả lời 1 câu (`assessment_attempt_answers`). Cột phân loại là SNAPSHOT lúc làm bài —
/// trùng lặp có chủ đích để sửa đề về sau không làm sai lệch phân tích cũ.
/// </summary>
public partial class AssessmentAttemptAnswer
{
    public Guid Id { get; set; }

    public Guid AttemptId { get; set; }

    public Guid QuestionId { get; set; }

    /// <summary>Cùng dạng CorrectAnswer. NULL = bỏ trống, khác trả lời sai.</summary>
    public string? GivenAnswer { get; set; }

    public bool IsCorrect { get; set; }

    public decimal EarnedPoints { get; set; }

    public int? ChapterId { get; set; }

    /// <summary>Khớp chapters.slug — gom nhóm khi phân tích.</summary>
    public string? ChapterSlug { get; set; }

    public string? Difficulty { get; set; }

    public string? QuestionFormat { get; set; }

    public int? TimeSpentSeconds { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual AssessmentAttempt? Attempt { get; set; }

    public virtual AssessmentQuestion? Question { get; set; }
}
