using System.ComponentModel.DataAnnotations;

namespace MV.DomainLayer.DTO.RequestModel.Practice;

public class SubmitPracticeRequest
{
    [Required]
    public Guid QuestionId { get; set; }

    /// <summary>Key phương án học sinh chọn (vd "A"). Null = bỏ qua, vẫn ghi nhận là chưa làm được.</summary>
    public string? GivenAnswer { get; set; }

    /// <summary>Phiên hỏi bài dẫn tới lượt luyện này — để biết luyện đến từ đâu.</summary>
    public Guid? SourceSessionId { get; set; }
}
