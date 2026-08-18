using System.Globalization;
using MV.DomainLayer.Constants;
using MV.DomainLayer.Entities;

namespace MV.ApplicationLayer.Services;

/// <summary>Chấm 1 câu: chỉ ĐÚNG/SAI khách quan. Kết luận trình độ là việc của AI.</summary>
public static class AssessmentGrader
{
    /// <summary>Bỏ trống luôn là sai, nhưng vẫn lưu null để AI phân biệt với trả lời sai.</summary>
    public static bool IsCorrect(AssessmentQuestion question, string? givenAnswer)
    {
        if (string.IsNullOrWhiteSpace(givenAnswer)) return false;

        // Essay: BE không chấm được -> false, AI đọc bài làm rồi đánh giá.
        if (!AssessmentQuestionFormat.IsAutoGraded(question.QuestionFormat)) return false;

        return question.QuestionFormat == AssessmentQuestionFormat.ShortAnswer
            ? MatchesShortAnswer(question, givenAnswer)
            : MatchesKeys(question, givenAnswer);
    }

    /// <summary>Tập key phải TRÙNG KHỚP — thiếu hoặc thừa đều sai, không có điểm từng phần.</summary>
    private static bool MatchesKeys(AssessmentQuestion question, string givenAnswer)
    {
        var expected = AssessmentQuestionValidator.SplitKeys(question.CorrectAnswer);
        var given = AssessmentQuestionValidator.SplitKeys(givenAnswer);

        return given.Count == expected.Count
            && expected.All(e => given.Contains(e, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>Khớp correct_answer hoặc accepted_answers, sau chuẩn hoá.</summary>
    private static bool MatchesShortAnswer(AssessmentQuestion question, string givenAnswer)
    {
        var given = Normalize(givenAnswer);
        if (given.Length == 0) return false;

        var candidates = new List<string> { question.CorrectAnswer };
        if (question.AcceptedAnswers is { Count: > 0 })
            candidates.AddRange(question.AcceptedAnswers);

        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            if (Normalize(candidate) == given) return true;

            // "0,5" = "0.5" — gõ kiểu nào cũng đúng.
            if (TryParseNumber(candidate, out var expectedNum) &&
                TryParseNumber(givenAnswer, out var givenNum) &&
                expectedNum == givenNum)
                return true;
        }

        return false;
    }

    /// <summary>Bỏ khoảng trắng thừa + về chữ thường.</summary>
    private static string Normalize(string value) =>
        string.Join(' ', value.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));

    /// <summary>Đọc số theo cả dấu phẩy và dấu chấm.</summary>
    private static bool TryParseNumber(string value, out decimal result) =>
        decimal.TryParse(
            value.Trim().Replace(',', '.'),
            NumberStyles.Number,
            CultureInfo.InvariantCulture,
            out result);
}
