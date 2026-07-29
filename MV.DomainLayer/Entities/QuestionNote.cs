using System;

namespace MV.DomainLayer.Entities;

/// <summary>
/// Note do HỌC SINH chủ đích tạo từ một lời giải trên Interactive Solution Canvas.
/// Bảng: question_notes. KHÁC chat_sessions/chat_histories (luồng hội thoại tự động) —
/// đây là artifact học sinh CHỌN giữ lại để ôn/đánh dấu. Xoá note không đụng lịch sử chat.
/// SolutionSteps là snapshot JSONB các bước canvas -> mở lại render được, không gọi AI lại.
/// </summary>
public partial class QuestionNote
{
    public Guid NoteId { get; set; }

    public string UserId { get; set; } = null!;

    public Guid? SourceSessionId { get; set; }

    public string Title { get; set; } = null!;

    public string? ProblemText { get; set; }

    public string? ProblemImageUrl { get; set; }

    public string SolutionSteps { get; set; } = "[]";

    public string? AnswerSummary { get; set; }

    public string? PersonalNote { get; set; }

    /// <summary>
    /// Ghi chú theo từng bước giải
    /// </summary>
    public string StepNotes { get; set; } = "{}";

    public string? Subject { get; set; }

    public int? GradeLevel { get; set; }

    public string? Chapter { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual User? User { get; set; }
}
