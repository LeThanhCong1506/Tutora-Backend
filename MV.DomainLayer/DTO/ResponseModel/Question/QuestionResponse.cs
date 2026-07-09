namespace MV.DomainLayer.DTO.ResponseModel.Question;

/// <summary>
/// Câu hỏi trả về cho staff/admin (CMS). KHÔNG expose embedding (vector 768 số vô nghĩa với UI).
/// HasEmbedding cho biết câu đã được vector hóa chưa.
/// </summary>
public class QuestionResponse
{
    public Guid Id { get; set; }
    public int SubjectId { get; set; }
    public string? SubjectName { get; set; }
    public int GradeLevelId { get; set; }
    public string? GradeName { get; set; }

    public int? ChapterId { get; set; }
    public string? ChapterName { get; set; }     // hiển thị "Ứng dụng đạo hàm"
    public int? QuestionTypeId { get; set; }
    public string? QuestionTypeName { get; set; } // hiển thị "Trắc nghiệm"

    /// <summary>NHAN_BIET | THONG_HIEU | VAN_DUNG | VAN_DUNG_CAO.</summary>
    public string? Difficulty { get; set; }
    public string Content { get; set; } = null!;
    public string? Solution { get; set; }
    public string? SolutionSource { get; set; }
    public List<string> ImageUrls { get; set; } = new();
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
