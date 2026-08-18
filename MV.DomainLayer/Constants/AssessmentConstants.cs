namespace MV.DomainLayer.Constants;

/// <summary>Khớp CHECK constraint trong V20260818b — sửa 1 chỗ phải sửa cả 2.</summary>
public static class AssessmentStatus
{
    /// <summary>Đang soạn — học sinh không thấy.</summary>
    public const string Draft = "draft";

    /// <summary>Đã phát hành — học sinh làm được.</summary>
    public const string Published = "published";

    /// <summary>Ngừng dùng — giữ lại để tra kết quả cũ.</summary>
    public const string Archived = "archived";

    public static readonly string[] All = { Draft, Published, Archived };

    public static bool IsValid(string? value) => value != null && All.Contains(value);
}

/// <summary>Cách chấm điểm. Suy từ question_types.slug — xem QuestionTypeFormatMapper.</summary>
public static class AssessmentQuestionFormat
{
    /// <summary>1 đáp án đúng. CorrectAnswer = 1 key, vd "A".</summary>
    public const string SingleChoice = "single_choice";

    /// <summary>Nhiều đáp án đúng. CorrectAnswer = CSV key, vd "A,C".</summary>
    public const string MultiChoice = "multi_choice";

    /// <summary>options = các mệnh đề; CorrectAnswer = CSV key mệnh đề ĐÚNG (vắng = sai).</summary>
    public const string TrueFalse = "true_false";

    /// <summary>Nhập tay. Không options; AcceptedAnswers = cách viết khác cũng đúng.</summary>
    public const string ShortAnswer = "short_answer";

    /// <summary>Tự luận/ghép đôi/sắp xếp... KHÔNG auto-chấm; đáp án để AI phân tích.</summary>
    public const string Essay = "essay";

    public static readonly string[] All = { SingleChoice, MultiChoice, TrueFalse, ShortAnswer, Essay };

    public static bool IsValid(string? value) => value != null && All.Contains(value);

    /// <summary>true nếu cần danh sách phương án.</summary>
    public static bool RequiresOptions(string format) => format is SingleChoice or MultiChoice or TrueFalse;

    /// <summary>true nếu BE chấm đúng/sai được. Essay -> để AI đánh giá.</summary>
    public static bool IsAutoGraded(string format) => format != Essay;

    /// <summary>true nếu cho nhiều đáp án đúng.</summary>
    public static bool AllowsMultipleKeys(string format) => format is MultiChoice or TrueFalse;
}

/// <summary>4 cấp độ khó Bộ GD.</summary>
public static class AssessmentDifficulty
{
    public static readonly string[] All = { "NHAN_BIET", "THONG_HIEU", "VAN_DUNG", "VAN_DUNG_CAO" };

    public static bool IsValid(string? value) => value != null && All.Contains(value);
}

/// <summary>Trạng thái 1 lần làm bài. Khớp CHECK trong V20260818c.</summary>
public static class AssessmentAttemptStatus
{
    /// <summary>Đang làm — chưa nộp.</summary>
    public const string InProgress = "in_progress";

    /// <summary>Đã nộp và đã chấm.</summary>
    public const string Submitted = "submitted";

    /// <summary>Bỏ dở / quá giờ.</summary>
    public const string Abandoned = "abandoned";

    public static readonly string[] All = { InProgress, Submitted, Abandoned };

    public static bool IsValid(string? value) => value != null && All.Contains(value);
}

/// <summary>Trạng thái phân tích AI. ĐỘC LẬP với chấm điểm — AI lỗi bài vẫn có điểm.</summary>
public static class AssessmentAnalysisStatus
{
    /// <summary>Chờ gửi AI.</summary>
    public const string Pending = "pending";

    /// <summary>Đang gửi AI — chống 2 worker cùng xử lý.</summary>
    public const string Processing = "processing";

    public const string Done = "done";

    /// <summary>AI lỗi, retry được.</summary>
    public const string Failed = "failed";

    public static readonly string[] All = { Pending, Processing, Done, Failed };

    public static bool IsValid(string? value) => value != null && All.Contains(value);
}

/// <summary>Mức trình độ AI kết luận. KHÔNG suy từ ngưỡng điểm — đề không có ngưỡng đạt.</summary>
public static class ProficiencyLevel
{
    public const string Beginner = "beginner";
    public const string Developing = "developing";
    public const string Proficient = "proficient";
    public const string Advanced = "advanced";

    public static readonly string[] All = { Beginner, Developing, Proficient, Advanced };

    public static bool IsValid(string? value) => value != null && All.Contains(value);
}
