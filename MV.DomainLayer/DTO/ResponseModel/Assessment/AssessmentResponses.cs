namespace MV.DomainLayer.DTO.ResponseModel.Assessment;

/// <summary>Bộ đề + số câu đã gán, tổng điểm.</summary>
public class AssessmentResponse
{
    public Guid Id { get; set; }
    public string Title { get; set; } = null!;
    public string? Description { get; set; }

    public int SubjectId { get; set; }
    public string? SubjectName { get; set; }
    public int GradeLevelId { get; set; }
    public string? GradeName { get; set; }

    public int? QuestionCount { get; set; }
    public int? DurationMinutes { get; set; }
    public bool ShuffleQuestions { get; set; }
    public bool ShuffleOptions { get; set; }
    public bool ShowResult { get; set; }
    public string Status { get; set; } = null!;

    /// <summary>Số câu đã gán (khác QuestionCount = số câu phải làm).</summary>
    public int AssignedQuestionCount { get; set; }

    /// <summary>Tổng điểm câu đã gán.</summary>
    public decimal TotalPoints { get; set; }

    /// <summary>Đủ câu so với QuestionCount -> phát hành được.</summary>
    public bool IsReady { get; set; }

    public string? CreatedBy { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>Chi tiết đề kèm câu hỏi theo thứ tự.</summary>
public class AssessmentDetailResponse : AssessmentResponse
{
    public List<AssessmentQuestionResponse> Questions { get; set; } = new();
}

public class AssessmentAnswerOptionResponse
{
    public string Key { get; set; } = "";
    public string Text { get; set; } = "";
}

/// <summary>1 câu, bản cho CMS (CÓ đáp án). Bản học sinh là AttemptQuestionResponse.</summary>
public class AssessmentQuestionResponse
{
    public Guid Id { get; set; }
    public Guid AssessmentId { get; set; }
    public int DisplayOrder { get; set; }
    public decimal Points { get; set; }

    public string QuestionFormat { get; set; } = null!;

    public int? ChapterId { get; set; }
    public string? ChapterName { get; set; }
    public int? QuestionTypeId { get; set; }
    public string? QuestionTypeName { get; set; }
    public string? Difficulty { get; set; }

    public string Content { get; set; } = null!;
    public List<AssessmentAnswerOptionResponse>? AnswerOptions { get; set; }
    public string CorrectAnswer { get; set; } = null!;
    public List<string>? AcceptedAnswers { get; set; }
    public string? Explanation { get; set; }
    public List<string> ImageUrls { get; set; } = new();

    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
