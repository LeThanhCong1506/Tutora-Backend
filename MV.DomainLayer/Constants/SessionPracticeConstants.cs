namespace MV.DomainLayer.Constants;

/// <summary>
/// Trạng thái bộ bài tập. Phải khớp CHECK constraint `practice_sets_status_check`.
/// </summary>
public static class SessionPracticeSetStatus
{
    /// <summary>Mới sinh, chỉ gia sư thấy — đang duyệt/sửa.</summary>
    public const string Draft = "draft";

    /// <summary>Đã gửi, học sinh thấy và làm được.</summary>
    public const string Sent = "sent";
}

/// <summary>
/// Loại câu hỏi. Phải khớp CHECK constraint `practice_questions_format_check`.
/// </summary>
public static class SessionPracticeQuestionFormat
{
    /// <summary>Trắc nghiệm — đối chiếu CorrectAnswer, phản hồi đúng/sai tức thì.</summary>
    public const string MultipleChoice = "mc";

    /// <summary>Tự luận — học sinh trình bày, gia sư nhận xét trực tiếp trong buổi.</summary>
    public const string Essay = "essay";
}

/// <summary>
/// Trạng thái trích xuất nội dung tài liệu (`learning_material_contents.status`).
/// </summary>
public static class MaterialContentStatus
{
    public const string Processing = "processing";
    public const string Ready = "ready";
    public const string Failed = "failed";
}
