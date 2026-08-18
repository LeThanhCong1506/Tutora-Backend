using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel.Assessment;

namespace MV.ApplicationLayer.Services;

/// <summary>Kiểm đáp án theo cách chấm. Phải kiểm ở service, không chỉ dựa FE.</summary>
public static class AssessmentQuestionValidator
{
    /// <summary>Null nếu hợp lệ, ngược lại là thông báo lỗi cho admin.</summary>
    public static string? Validate(CreateAssessmentQuestionRequest r, string format)
    {
        if (r.Difficulty != null && !AssessmentDifficulty.IsValid(r.Difficulty))
            return "Độ khó không hợp lệ.";

        var correct = r.CorrectAnswer?.Trim();
        if (string.IsNullOrEmpty(correct))
            return format == AssessmentQuestionFormat.Essay
                ? "Vui lòng nhập đáp án mẫu để AI đối chiếu."
                : "Vui lòng nhập đáp án đúng.";

        if (AssessmentQuestionFormat.RequiresOptions(format))
            return ValidateChoice(r, format, correct);

        // Essay: đáp án mẫu là đủ, không ràng buộc gì thêm.
        return format == AssessmentQuestionFormat.Essay ? null : ValidateShortAnswer(r);
    }

    private static string? ValidateChoice(CreateAssessmentQuestionRequest r, string format, string correct)
    {
        var options = r.AnswerOptions?
            .Where(o => !string.IsNullOrWhiteSpace(o.Key) && !string.IsNullOrWhiteSpace(o.Text))
            .ToList() ?? new List<AssessmentAnswerOptionRequest>();

        if (options.Count < 2)
            return "Cần ít nhất 2 phương án có nội dung.";

        var keys = options.Select(o => o.Key.Trim()).ToList();
        if (keys.Distinct(StringComparer.OrdinalIgnoreCase).Count() != keys.Count)
            return "Ký hiệu phương án bị trùng.";

        var correctKeys = SplitKeys(correct);
        if (correctKeys.Count == 0)
            return "Vui lòng chọn đáp án đúng.";

        if (!AssessmentQuestionFormat.AllowsMultipleKeys(format) && correctKeys.Count > 1)
            return "Loại này chỉ được chọn 1 đáp án đúng.";

        var unknown = correctKeys.FirstOrDefault(k => !keys.Contains(k, StringComparer.OrdinalIgnoreCase));
        return unknown != null
            ? $"Đáp án đúng \"{unknown}\" không nằm trong danh sách phương án."
            : null;
    }

    private static string? ValidateShortAnswer(CreateAssessmentQuestionRequest r)
    {
        if (r.AnswerOptions is { Count: > 0 })
            return "Câu trả lời ngắn không có phương án lựa chọn.";

        var accepted = r.AcceptedAnswers?.Where(a => !string.IsNullOrWhiteSpace(a)).ToList();
        return accepted != null && accepted.Count > 20 ? "Tối đa 20 đáp án được chấp nhận." : null;
    }

    /// <summary>"A, C" -> ["A","C"], bỏ rỗng và trùng.</summary>
    public static List<string> SplitKeys(string csv) => csv
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();
}
