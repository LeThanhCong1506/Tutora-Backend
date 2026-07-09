namespace MV.DomainLayer.DTO.ResponseModel.Question;

/// <summary>
/// Kết quả upload PDF -> extract câu hỏi. Các câu được lưu ở trạng thái
/// pending_review, chờ staff duyệt trước khi publish.
/// </summary>
public class UploadPdfResponse
{
    public Guid SourceDocumentId { get; set; }
    public string FileName { get; set; } = null!;
    public int? PageCount { get; set; }

    /// <summary>Số câu AI extract + lưu được.</summary>
    public int QuestionsExtracted { get; set; }

    /// <summary>Số câu đã embed thành vector (best-effort).</summary>
    public int QuestionsEmbedded { get; set; }

    /// <summary>pending | processing | done | failed.</summary>
    public string Status { get; set; } = null!;

    public string? Message { get; set; }

    /// <summary>Danh sách câu vừa extract (pending_review) để staff edit/duyệt ngay.</summary>
    public List<QuestionResponse> Questions { get; set; } = new();
}
