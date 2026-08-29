using System.ComponentModel.DataAnnotations;
using MV.DomainLayer.Entities;

namespace MV.DomainLayer.DTO.RequestModel;

/// <summary>Gia sư bấm "Tạo câu hỏi": chọn tài liệu + gõ yêu cầu tự do.</summary>
public class GenerateSessionPracticeRequest
{
    [Required]
    [MinLength(1, ErrorMessage = "Chọn ít nhất 1 tài liệu nguồn.")]
    public List<int> MaterialIds { get; set; } = [];

    [Required(ErrorMessage = "Nhập yêu cầu cho AI.")]
    [MaxLength(1000)]
    public string Prompt { get; set; } = string.Empty;

    /// <summary>Buổi học đang diễn ra. Null nếu gia sư soạn ngoài buổi.</summary>
    public int? ClassSessionId { get; set; }
}

/// <summary>Gia sư sửa 1 câu trước khi gửi.</summary>
public class UpdateSessionPracticeQuestionRequest
{
    [Required]
    public string Content { get; set; } = string.Empty;

    public List<AnswerOption>? AnswerOptions { get; set; }

    public string? CorrectAnswer { get; set; }

    public string? Explanation { get; set; }
}

/// <summary>Học sinh trả lời 1 câu.</summary>
public class SubmitSessionPracticeAnswerRequest
{
    [Required(ErrorMessage = "Chưa có nội dung trả lời.")]
    public string Answer { get; set; } = string.Empty;
}
