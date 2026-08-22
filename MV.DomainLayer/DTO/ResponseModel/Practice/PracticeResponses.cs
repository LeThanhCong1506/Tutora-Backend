namespace MV.DomainLayer.DTO.ResponseModel.Practice;

/// <summary>
/// Câu mời luyện sau khi học sinh giải xong một bài.
/// KHÔNG kèm correct_answer — nếu không học sinh mở DevTools là thấy đáp án.
/// </summary>
public class PracticeQuestionResponse
{
    public Guid QuestionId { get; set; }
    public string Content { get; set; } = null!;
    public List<PracticeOptionResponse> Options { get; set; } = new();
    public string? ChapterName { get; set; }
    public string? Difficulty { get; set; }
}

public class PracticeOptionResponse
{
    public string Key { get; set; } = null!;
    public string Text { get; set; } = null!;
}

/// <summary>Kết quả chấm — đáp án và lời giải chỉ lộ SAU khi học sinh đã trả lời.</summary>
public class PracticeResultResponse
{
    public bool IsCorrect { get; set; }
    public string CorrectAnswer { get; set; } = null!;
    public string? Solution { get; set; }
    public string? Explanation { get; set; }

    /// <summary>Số câu đúng/tổng của chương này, để UI nói "em đã làm 3/5 câu đúng".</summary>
    public int ChapterCorrect { get; set; }
    public int ChapterTotal { get; set; }
}
