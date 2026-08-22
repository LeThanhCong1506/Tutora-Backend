namespace MV.DomainLayer.Entities;

/// <summary>
/// 1 lượt học sinh luyện 1 câu từ question bank. Bảng: practice_attempts.
///
/// Nguồn tín hiệu đúng/sai KHÁCH QUAN cho hồ sơ trình độ — khác StudentTopicSignal
/// vốn chỉ ghi được học sinh đã HỎI chương nào, không nói lên làm được hay không.
/// </summary>
public partial class PracticeAttempt
{
    public Guid Id { get; set; }

    public string UserId { get; set; } = null!;

    public Guid QuestionId { get; set; }

    /// <summary>Snapshot lúc làm — sửa/xoá câu trong bank sau này không làm sai thống kê cũ.</summary>
    public string? Chapter { get; set; }

    public int? GradeLevelId { get; set; }

    public string? Difficulty { get; set; }

    /// <summary>NULL = bỏ qua, không trả lời.</summary>
    public string? GivenAnswer { get; set; }

    public bool IsCorrect { get; set; }

    /// <summary>Phiên hỏi bài dẫn tới lượt luyện. NULL = luyện từ lộ trình.</summary>
    public Guid? SourceSessionId { get; set; }

    public DateTime CreatedAt { get; set; }
}
