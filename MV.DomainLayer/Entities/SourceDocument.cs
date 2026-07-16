using System;

namespace MV.DomainLayer.Entities;

/// <summary>
/// File PDF staff/admin upload để AI extract câu hỏi (bảng `source_documents`).
/// 1 PDF ≤ 20 trang (validate lúc upload). 1 document -> N questions.
/// </summary>
public partial class SourceDocument
{
    public Guid Id { get; set; }

    public string FileUrl { get; set; } = null!;

    public string FileName { get; set; } = null!;

    /// <summary>Tổng số trang; dùng reject nếu > 20.</summary>
    public int? PageCount { get; set; }

    /// <summary>Gợi ý mặc định môn cho các câu hỏi sinh ra.</summary>
    public int? DefaultSubjectId { get; set; }

    public int? DefaultGradeLevelId { get; set; }

    /// <summary>pending | processing | done | failed.</summary>
    public string Status { get; set; } = "pending";

    /// <summary>Số câu AI extract được từ file này.</summary>
    public int QuestionsExtracted { get; set; }

    public string? ErrorMessage { get; set; }

    public string? UploadedBy { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual ICollection<QuestionBank> Questions { get; set; } = new List<QuestionBank>();
}
