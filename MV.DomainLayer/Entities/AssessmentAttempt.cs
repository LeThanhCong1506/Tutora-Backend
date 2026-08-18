using System;

namespace MV.DomainLayer.Entities;

/// <summary>
/// 1 lần học sinh làm 1 đề (`assessment_attempts`). BE chấm khách quan; AI phân tích rồi
/// ghi vào <see cref="StudentProficiencyProfile"/>.
/// </summary>
public partial class AssessmentAttempt
{
    public Guid Id { get; set; }

    public Guid AssessmentId { get; set; }

    /// <summary>users.user_id — giống student_topic_signals/ai_credit.</summary>
    public string UserId { get; set; } = null!;

    /// <summary>in_progress | submitted | abandoned.</summary>
    public string Status { get; set; } = "in_progress";

    public DateTime StartedAt { get; set; }

    public DateTime? SubmittedAt { get; set; }

    /// <summary>Deadline chốt lúc bắt đầu. NULL = không giới hạn.</summary>
    public DateTime? ExpiresAt { get; set; }

    // Kết quả chấm

    public int TotalQuestions { get; set; }

    public int CorrectCount { get; set; }

    public decimal EarnedPoints { get; set; }

    public decimal MaxPoints { get; set; }

    /// <summary>earned/max*100 — SỐ ĐO, không so ngưỡng nào.</summary>
    public decimal? ScorePercent { get; set; }

    /// <summary>Thời gian làm (giây).</summary>
    public int? DurationSeconds { get; set; }

    // Phân tích của AI

    /// <summary>pending | processing | done | failed. ĐỘC LẬP với chấm điểm.</summary>
    public string AnalysisStatus { get; set; } = "pending";

    /// <summary>Nhận xét AI (markdown).</summary>
    public string? AnalysisSummary { get; set; }

    /// <summary>JSON thô, BE không parse.</summary>
    public string? AnalysisResult { get; set; }

    public string? AnalysisError { get; set; }

    public DateTime? AnalyzedAt { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual Assessment? Assessment { get; set; }

    public virtual ICollection<AssessmentAttemptAnswer> Answers { get; set; } = new List<AssessmentAttemptAnswer>();
}
