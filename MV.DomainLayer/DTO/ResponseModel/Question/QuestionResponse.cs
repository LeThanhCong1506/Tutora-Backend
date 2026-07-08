namespace MV.DomainLayer.DTO.ResponseModel.Question;

/// <summary>
/// Câu hỏi trả về cho staff/admin (CMS). KHÔNG expose embedding (vector 768 số vô nghĩa với UI).
/// HasEmbedding cho biết câu đã được vector hóa chưa.
/// </summary>
public class QuestionResponse
{
    public Guid Id { get; set; }
    public int SubjectId { get; set; }
    public int GradeLevelId { get; set; }
    public string? Chapter { get; set; }
    public string? ProblemType { get; set; }
    public short? Difficulty { get; set; }
    public string Content { get; set; } = null!;
    public string? Solution { get; set; }
    public string? SolutionSource { get; set; }
    public Guid? SourceDocumentId { get; set; }
    public int? SourcePage { get; set; }
    public string ReviewStatus { get; set; } = null!;
    public string? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }

    /// <summary>Đã embed thành vector chưa (embedded_hash == content_hash).</summary>
    public bool HasEmbedding { get; set; }

    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
