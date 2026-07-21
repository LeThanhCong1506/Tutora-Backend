namespace MV.DomainLayer.DTO.ResponseModel.Question;

/// <summary>
/// Câu hỏi trả cho trang Tài nguyên công khai (study-resources).
/// </summary>
public class PublicQuestionResponse
{
    public Guid Id { get; set; }

    public string Content { get; set; } = null!;
    public string? Solution { get; set; }
    public string? SolutionSource { get; set; }
    public List<string> ImageUrls { get; set; } = new();

    public string? Difficulty { get; set; }

    public string? SubjectName { get; set; }
    public string? ChapterName { get; set; }
    public string? QuestionTypeName { get; set; }

    public int LikeCount { get; set; }
    public int DislikeCount { get; set; }

    /// <summary>like / (like + dislike) * 100, làm tròn. 0 nếu chưa ai vote.</summary>
    public int HelpfulPercent { get; set; }

    public int? MyVote { get; set; }

    public DateTime? CreatedAt { get; set; }
}
