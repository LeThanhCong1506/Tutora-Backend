using System.ComponentModel.DataAnnotations;

namespace MV.DomainLayer.DTO.RequestModel.Question;

/// <summary>
/// Cập nhật câu hỏi. Nếu Content đổi -> hệ thống re-embed (vector mới).
/// Chỉ set field muốn đổi; field null = giữ nguyên (trừ Content/Subject/Grade bắt buộc).
/// </summary>
public class UpdateQuestionRequest
{
    [Required(ErrorMessage = "Môn học là bắt buộc")]
    public int SubjectId { get; set; }

    [Required(ErrorMessage = "Khối lớp là bắt buộc")]
    public int GradeLevelId { get; set; }

    public int? ChapterId { get; set; }

    public int? QuestionTypeId { get; set; }

    [RegularExpression("NHAN_BIET|THONG_HIEU|VAN_DUNG|VAN_DUNG_CAO",
        ErrorMessage = "Độ khó không hợp lệ.")]
    public string? Difficulty { get; set; }

    [Required(ErrorMessage = "Nội dung câu hỏi là bắt buộc")]
    [MinLength(5, ErrorMessage = "Nội dung câu hỏi quá ngắn")]
    public string Content { get; set; } = null!;

    public string? Solution { get; set; }

    public string? SolutionSource { get; set; }

    /// <summary>pending_review | published | rejected.</summary>
    public string? ReviewStatus { get; set; }
}
