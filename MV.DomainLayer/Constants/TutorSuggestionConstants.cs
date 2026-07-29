namespace MV.DomainLayer.Constants;

/// <summary>
/// Khoá cấu hình gợi ý gia sư trong bảng system_configs
/// </summary>
public static class TutorSuggestionConfigKeys
{
    /// <summary>Bật/tắt toàn bộ tính năng gợi ý gia sư ("true"/"false").</summary>
    public const string Enabled = "tutor_suggestion_enabled";

    /// <summary>Số bài cùng một chương TRONG MỘT PHIÊN thì mới hiện gợi ý.</summary>
    public const string MinChapterCount = "tutor_suggestion_min_chapter_count";

    /// <summary>Ngưỡng confidence của classifier để tín hiệu được tính (0.0-1.0).</summary>
    public const string MinConfidence = "tutor_suggestion_min_confidence";

    /// <summary>Số card gia sư trả về cho slider.</summary>
    public const string TopK = "tutor_suggestion_top_k";
}

/// <summary>
/// Mặc định khi admin chưa cấu hình.
/// </summary>
public static class TutorSuggestionDefaults
{
    public const bool Enabled = true;
    public const int MinChapterCount = 2;
    public const float MinConfidence = 0.5f;
    public const int TopK = 6;

    /// <summary>Giới hạn số chương yếu hiển thị dạng chip trong modal.</summary>
    public const int MaxWeakChapters = 3;
}

/// <summary>Lý do KHÔNG hiện gợi ý — trả về cho FE/analytics để biết vì sao im lặng.</summary>
public static class TutorSuggestionSkipReasons
{
    public const string DisabledByAdmin = "disabled_by_admin";
    public const string DisabledByUser = "disabled_by_user";
    public const string NotStudent = "not_student";
    public const string AlreadyBooked = "already_booked";
    public const string NoSignal = "no_signal";
    public const string BelowThreshold = "below_threshold";
    public const string NoTutorFound = "no_tutor_found";
}

/// <summary>
/// Kiểu nút hành động trên card gia sư. Học sinh dưới 16 hoặc do phụ huynh quản lý không
/// tự đặt lịch được -> đổi sang chia sẻ hồ sơ cho phụ huynh thay vì chặn hẳn gợi ý.
/// </summary>
public static class TutorSuggestionCtaModes
{
    public const string Book = "book";
    public const string Share = "share";
}
