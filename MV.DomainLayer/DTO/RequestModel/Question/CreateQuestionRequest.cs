using System.ComponentModel.DataAnnotations;

namespace MV.DomainLayer.DTO.RequestModel.Question;

/// <summary>
/// Staff/admin tạo 1 câu hỏi vào question bank. Sau khi tạo, hệ thống gọi
/// tutora-ai để embed (content -> vector) rồi lưu embedding.
/// </summary>
public class CreateQuestionRequest
{
    [Required(ErrorMessage = "Môn học là bắt buộc")]
    public int SubjectId { get; set; }

    [Required(ErrorMessage = "Khối lớp là bắt buộc")]
    public int GradeLevelId { get; set; }

    /// <summary>FK chương (chapters.id). Chương gắn môn+lớp.</summary>
    public int? ChapterId { get; set; }

    /// <summary>FK loại câu hỏi (question_types.id).</summary>
    public int? QuestionTypeId { get; set; }

    /// <summary>4 cấp Bộ GD: NHAN_BIET | THONG_HIEU | VAN_DUNG | VAN_DUNG_CAO.</summary>
    [RegularExpression("NHAN_BIET|THONG_HIEU|VAN_DUNG|VAN_DUNG_CAO",
        ErrorMessage = "Độ khó không hợp lệ.")]
    public string? Difficulty { get; set; }

    [Required(ErrorMessage = "Nội dung câu hỏi là bắt buộc")]
    [MinLength(5, ErrorMessage = "Nội dung câu hỏi quá ngắn")]
    public string Content { get; set; } = null!;

    /// <summary>Lời giải mẫu (thầy cô/Bộ GD). Optional.</summary>
    public string? Solution { get; set; }

    public string? SolutionSource { get; set; }

    /// <summary>pending_review | published. Mặc định pending_review (chờ duyệt).</summary>
    public string? ReviewStatus { get; set; }
}
