using MV.DomainLayer.Entities;

namespace MV.DomainLayer.DTO.ResponseModel;

/// <summary>Bộ bài tập kèm câu hỏi — dùng cho cả gia sư và học sinh.</summary>
public class SessionPracticeSetResponse
{
    public Guid Id { get; set; }
    public int BookingId { get; set; }
    public int? ClassSessionId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Prompt { get; set; }
    /// <summary>draft | sent.</summary>
    public string Status { get; set; } = string.Empty;
    public DateTime? SentAt { get; set; }
    public DateTime CreatedAt { get; set; }
    /// <summary>Tài liệu nguồn AI đã đọc — hiện "Trích từ ..." ở FE.</summary>
    public List<SessionPracticeMaterialRef> Materials { get; set; } = [];
    public List<SessionPracticeQuestionResponse> Questions { get; set; } = [];
}

public class SessionPracticeMaterialRef
{
    public int MaterialId { get; set; }
    public string Title { get; set; } = string.Empty;
}

public class SessionPracticeQuestionResponse
{
    public Guid Id { get; set; }
    public Guid SetId { get; set; }
    public int DisplayOrder { get; set; }
    /// <summary>mc | essay.</summary>
    public string QuestionFormat { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public List<AnswerOption>? AnswerOptions { get; set; }

    /// <summary>
    /// CHỈ trả cho GIA SƯ. Với học sinh luôn null cho tới khi em đã trả lời câu đó —
    /// nếu không thì mở DevTools là thấy đáp án, bài tập thành vô nghĩa.
    /// </summary>
    public string? CorrectAnswer { get; set; }

    /// <summary>Cùng quy tắc che như <see cref="CorrectAnswer"/>.</summary>
    public string? Explanation { get; set; }

    public int? SourceMaterialId { get; set; }
    public string? SourceMaterialTitle { get; set; }
    public int? SourcePage { get; set; }

    /// <summary>Bài làm của học sinh đang xem. Null nếu chưa làm / người xem là gia sư.</summary>
    public SessionPracticeAnswerResponse? MyAnswer { get; set; }
}

public class SessionPracticeAnswerResponse
{
    public string Answer { get; set; } = string.Empty;
    public bool? IsCorrect { get; set; }
    public DateTime AnsweredAt { get; set; }
}

/// <summary>Trạng thái trích xuất nội dung tài liệu — FE chặn chọn khi chưa 'ready'.</summary>
public class MaterialContentStatusResponse
{
    public int MaterialId { get; set; }
    public string Status { get; set; } = string.Empty;
    public int? PageCount { get; set; }
    public string? ErrorMessage { get; set; }
}
