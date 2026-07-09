namespace MV.DomainLayer.DTO.ResponseModel.Question;

/// <summary>1 lần upload PDF (lịch sử trích xuất).</summary>
public class SourceDocumentResponse
{
    public Guid Id { get; set; }
    public string FileName { get; set; } = null!;
    public string FileUrl { get; set; } = null!;
    public int? PageCount { get; set; }
    public int? DefaultSubjectId { get; set; }
    public int? DefaultGradeLevelId { get; set; }
    public string Status { get; set; } = null!;   // pending|processing|done|failed
    public int QuestionsExtracted { get; set; }
    public string? ErrorMessage { get; set; }
    public string? UploadedBy { get; set; }
    public DateTime? CreatedAt { get; set; }
}

/// <summary>Chi tiết 1 lần upload: thông tin file + các câu đã tách.</summary>
public class SourceDocumentDetailResponse : SourceDocumentResponse
{
    public List<QuestionResponse> Questions { get; set; } = new();
}
