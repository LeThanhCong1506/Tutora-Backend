using MV.DomainLayer.Constants;

namespace MV.ApplicationLayer.Services;

/// <summary>Map question_types.slug -> cách chấm. Slug lạ -> tự luận (AI phân tích, không auto-chấm).</summary>
public static class QuestionTypeFormatMapper
{
    private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["trac_nghiem"]  = AssessmentQuestionFormat.SingleChoice,
        ["nhieu_dap_an"] = AssessmentQuestionFormat.MultiChoice,
        ["dung_sai"]     = AssessmentQuestionFormat.TrueFalse,
        ["dien_khuyet"]  = AssessmentQuestionFormat.ShortAnswer,
    };

    /// <summary>Mọi loại đều dùng được; loại không nằm trong map là tự luận.</summary>
    public static string Resolve(string? slug)
        => slug != null && Map.TryGetValue(slug, out var f) ? f : AssessmentQuestionFormat.Essay;
}
