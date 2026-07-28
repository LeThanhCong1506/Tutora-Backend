using System.Text.Json;

namespace MV.DomainLayer.DTO.ResponseModel;

/// <summary>
/// Note trả về FE. SolutionSteps là JsonElement (đọc từ cột jsonb) -> FE render canvas.
/// Danh sách note (list) có thể bỏ qua SolutionSteps để nhẹ; chi tiết (getById) trả đủ.
/// </summary>
public class QuestionNoteResponse
{
    public string NoteId { get; set; } = null!;
    public string? SourceSessionId { get; set; }
    public string Title { get; set; } = null!;
    public string? ProblemText { get; set; }
    public string? ProblemImageUrl { get; set; }
    public JsonElement? SolutionSteps { get; set; }
    public string? AnswerSummary { get; set; }
    public string? PersonalNote { get; set; }
    public JsonElement? StepNotes { get; set; }
    public string? Subject { get; set; }
    public int? GradeLevel { get; set; }
    public string? Chapter { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
