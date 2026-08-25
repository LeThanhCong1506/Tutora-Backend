using System.ComponentModel.DataAnnotations;

namespace MV.DomainLayer.DTO.RequestModel.Practice;

public class SubmitPracticeRequest
{
    [Required]
    public Guid QuestionId { get; set; }

    /// <summary>
    /// Học sinh TỰ chấm sau khi xem lời giải: correct | partial | wrong.
    /// </summary>
    [Required]
    public string SelfAssessment { get; set; } = null!;

    /// <summary>Phiên hỏi bài dẫn tới lượt luyện này.</summary>
    public Guid? SourceSessionId { get; set; }
}
