namespace MV.DomainLayer.DTO.ResponseModel.Practice;

/// <summary>
/// Câu mời luyện sau khi học sinh giải xong một bài.
///
/// Dạng TỰ LUẬN: học sinh tự làm ra giấy rồi đối chiếu lời giải mẫu.
/// </summary>
public class PracticeQuestionResponse
{
    public Guid QuestionId { get; set; }
    public string Content { get; set; } = null!;
    public string? ChapterName { get; set; }
    public string? Difficulty { get; set; }

    /// <summary>Lời giải mẫu — FE chỉ hiện SAU khi học sinh bấm "Xem lời giải".</summary>
    public string? Solution { get; set; }
}

/// <summary>
/// Xác nhận đã ghi lượt luyện.
/// </summary>
public class PracticeResultResponse
{
}
