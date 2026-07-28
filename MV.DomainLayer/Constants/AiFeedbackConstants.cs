namespace MV.DomainLayer.Constants;

/// <summary>Giá trị vote hợp lệ</summary>
public static class FeedbackVote
{
    public const short Like = 1;
    public const short Dislike = -1;

    public static bool IsValid(short vote) => vote is Like or Dislike;
}

/// <summary>
/// Lý do không hài lòng với LỜI GIẢI của AI. S
/// </summary>
public static class AiMessageFeedbackReasons
{
    public const string WrongAnswer = "sai_dap_an";
    public const string HardToUnderstand = "kho_hieu";
    /// <summary>Giải vượt/dưới chương trình lớp học sinh đang học.</summary>
    public const string WrongGradeLevel = "sai_lop";
    public const string MissingSteps = "thieu_buoc";
    public const string Other = "khac";

    public static readonly string[] All =
        { WrongAnswer, HardToUnderstand, WrongGradeLevel, MissingSteps, Other };

    public static bool IsValid(string? reason) => reason is null || All.Contains(reason);
}

/// <summary>
/// Lý do không hài lòng với GỢI Ý GIA SƯ.
/// </summary>
public static class TutorSuggestionFeedbackReasons
{
    /// <summary>Gia sư không dạy môn/chương đang cần — lỗi thuật toán ghép.</summary>
    public const string WrongSubject = "sai_mon_chuong";
    public const string TooExpensive = "gia_cao";
    public const string ScheduleMismatch = "khong_hop_lich";
    /// <summary>Tín hiệu "đang vướng" sai — học sinh không cần gia sư.</summary>
    public const string NotNeeded = "khong_can_gia_su";
    public const string Other = "khac";

    public static readonly string[] All =
        { WrongSubject, TooExpensive, ScheduleMismatch, NotNeeded, Other };

    public static bool IsValid(string? reason) => reason is null || All.Contains(reason);
}
