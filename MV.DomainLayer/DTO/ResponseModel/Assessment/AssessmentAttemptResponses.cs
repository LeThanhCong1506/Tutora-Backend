namespace MV.DomainLayer.DTO.ResponseModel.Assessment;

/// <summary>1 câu như học sinh thấy — KHÔNG có đáp án. Tách DTO để không thể lộ.</summary>
public class AttemptQuestionResponse
{
    public Guid Id { get; set; }
    public int DisplayOrder { get; set; }
    public decimal Points { get; set; }
    public string QuestionFormat { get; set; } = null!;
    public string Content { get; set; } = null!;
    public List<AssessmentAnswerOptionResponse>? AnswerOptions { get; set; }
    public List<string> ImageUrls { get; set; } = new();

    /// <summary>Trả lời đã lưu khi tiếp tục bài dở.</summary>
    public string? GivenAnswer { get; set; }
}

/// <summary>Bài đang làm: đề + câu hỏi + deadline.</summary>
public class AttemptInProgressResponse
{
    public Guid AttemptId { get; set; }
    public Guid AssessmentId { get; set; }
    public string Title { get; set; } = null!;
    public string? Description { get; set; }
    public string? SubjectName { get; set; }
    public string? GradeName { get; set; }

    public int? DurationMinutes { get; set; }
    public DateTime StartedAt { get; set; }
    /// <summary>Null = không giới hạn.</summary>
    public DateTime? ExpiresAt { get; set; }

    public List<AttemptQuestionResponse> Questions { get; set; } = new();
}

/// <summary>1 câu sau khi nộp. Đáp án + giải thích LUÔN có.</summary>
public class AttemptAnswerResultResponse
{
    public Guid QuestionId { get; set; }
    public int DisplayOrder { get; set; }
    public string Content { get; set; } = null!;
    public string QuestionFormat { get; set; } = null!;
    public List<AssessmentAnswerOptionResponse>? AnswerOptions { get; set; }

    public string? GivenAnswer { get; set; }
    public bool IsCorrect { get; set; }
    public decimal EarnedPoints { get; set; }
    public decimal Points { get; set; }

    public string? CorrectAnswer { get; set; }
    public string? Explanation { get; set; }

    public string? ChapterName { get; set; }
    public string? Difficulty { get; set; }
}

/// <summary>Kết quả 1 lần làm. CỐ Ý không có cờ đạt/không đạt.</summary>
public class AttemptResultResponse
{
    public Guid AttemptId { get; set; }
    public Guid AssessmentId { get; set; }
    public string Title { get; set; } = null!;
    public string? SubjectName { get; set; }
    public string? GradeName { get; set; }
    public string Status { get; set; } = null!;

    public int TotalQuestions { get; set; }
    public int CorrectCount { get; set; }
    public decimal EarnedPoints { get; set; }
    public decimal MaxPoints { get; set; }
    public decimal? ScorePercent { get; set; }
    public int? DurationSeconds { get; set; }

    public DateTime StartedAt { get; set; }
    public DateTime? SubmittedAt { get; set; }

    /// <summary>FE poll để hiện nhận xét khi xong.</summary>
    public string AnalysisStatus { get; set; } = null!;
    public string? AnalysisSummary { get; set; }
    /// <summary>JSON thô AI trả về.</summary>
    public string? AnalysisResult { get; set; }
    public DateTime? AnalyzedAt { get; set; }

    /// <summary>Đề có cho xem ĐIỂM hay không. Đáp án luôn xem được.</summary>
    public bool ShowResult { get; set; }

    /// <summary>Rỗng ở endpoint lịch sử.</summary>
    public List<AttemptAnswerResultResponse> Answers { get; set; } = new();
}

/// <summary>Dữ kiện thô gửi AI. BE không kết luận gì, AI tự rút ra trình độ.</summary>
public class AttemptAnalysisInputResponse
{
    public Guid AttemptId { get; set; }
    public string UserId { get; set; } = null!;
    public int SubjectId { get; set; }
    public string? SubjectName { get; set; }
    public int GradeLevelId { get; set; }
    public string? GradeName { get; set; }
    public string AssessmentTitle { get; set; } = null!;

    public int TotalQuestions { get; set; }
    public int CorrectCount { get; set; }
    public decimal EarnedPoints { get; set; }
    public decimal MaxPoints { get; set; }
    public decimal? ScorePercent { get; set; }
    public int? DurationSeconds { get; set; }

    /// <summary>Số lần đã đánh giá môn này.</summary>
    public int AttemptCount { get; set; }

    public List<AnalysisItemResponse> Items { get; set; } = new();

    /// <summary>Theo chương — AI chỉ ra lỗ hổng.</summary>
    public List<AnalysisChapterStatResponse> ChapterStats { get; set; } = new();

    /// <summary>Theo độ khó — phân biệt mất gốc với yếu vận dụng cao.</summary>
    public List<AnalysisDifficultyStatResponse> DifficultyStats { get; set; } = new();
}

/// <summary>1 câu gửi AI: đề, đáp án đúng, học sinh trả lời gì.</summary>
public class AnalysisItemResponse
{
    public int DisplayOrder { get; set; }
    public string Content { get; set; } = null!;
    public string QuestionFormat { get; set; } = null!;
    public string? ChapterName { get; set; }
    public string? ChapterSlug { get; set; }
    public string? Difficulty { get; set; }
    public string CorrectAnswer { get; set; } = null!;
    public string? GivenAnswer { get; set; }
    /// <summary>Bỏ trống, khác hẳn trả lời sai.</summary>
    public bool Skipped { get; set; }
    public bool IsCorrect { get; set; }
    public int? TimeSpentSeconds { get; set; }
}

public class AnalysisChapterStatResponse
{
    public int? ChapterId { get; set; }
    public string? ChapterName { get; set; }
    public string? ChapterSlug { get; set; }
    public int Total { get; set; }
    public int Correct { get; set; }
    public int Skipped { get; set; }
}

public class AnalysisDifficultyStatResponse
{
    public string? Difficulty { get; set; }
    public int Total { get; set; }
    public int Correct { get; set; }
    public int Skipped { get; set; }
}

/// <summary>Profile trình độ theo môn — AI giải bài đọc mỗi lần trả lời.</summary>
public class ProficiencyProfileResponse
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = null!;
    public int SubjectId { get; set; }
    public string? SubjectName { get; set; }
    public int? GradeLevelId { get; set; }
    public string? GradeName { get; set; }

    public string? Level { get; set; }
    public string? Summary { get; set; }
    /// <summary>JSON thô.</summary>
    public string? Strengths { get; set; }
    public string? Weaknesses { get; set; }
    public string? RecommendedPath { get; set; }

    public Guid? SourceAttemptId { get; set; }
    public int AttemptCount { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
