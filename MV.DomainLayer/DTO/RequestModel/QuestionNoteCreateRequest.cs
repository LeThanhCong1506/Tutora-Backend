using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace MV.DomainLayer.DTO.RequestModel;

public class QuestionNoteCreateRequest
{
    [Required]
    [MaxLength(255)]
    public string Title { get; set; } = null!;

    /// <summary>Phiên chat nguồn (nếu lưu từ 1 lời giải trong chat).</summary>
    public Guid? SourceSessionId { get; set; }

    public string? ProblemText { get; set; }

    public string? ProblemImageUrl { get; set; }

    /// <summary>Mảng SolutionStep — giữ nguyên shape FE render (index/title/explanation/formulas/goal/detailed/hints).</summary>
    public JsonElement? SolutionSteps { get; set; }

    public string? AnswerSummary { get; set; }

    public string? PersonalNote { get; set; }

    public string? Subject { get; set; }

    public int? GradeLevel { get; set; }

    public string? Chapter { get; set; }
}
