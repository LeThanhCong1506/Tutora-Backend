namespace MV.DomainLayer.DTO.ResponseModel;

/// <summary>
/// Gợi ý gia sư cho học sinh đang vướng một chương trong phiên giải bài hiện tại.
/// </summary>
public class StudentTutorSuggestionResponse
{
    public bool ShouldShow { get; set; }

    /// <summary>
    /// Vì sao không hiện — xem <see cref="MV.DomainLayer.Constants.TutorSuggestionSkipReasons"/>.
    /// Dùng cho debug/analytics;
    /// </summary>
    public string? SkipReason { get; set; }

    /// <summary>"book" | "share" — xem TutorSuggestionCtaModes.</summary>
    public string CtaMode { get; set; } = MV.DomainLayer.Constants.TutorSuggestionCtaModes.Book;

    /// <summary>Câu cho thanh gợi ý</summary>
    public string BannerText { get; set; } = "";

    public string ModalTitle { get; set; } = "";

    public string ModalSubtitle { get; set; } = "";

    /// <summary>Chương đang vướng (tối đa 3), hiện dạng chip trong modal.</summary>
    public List<WeakChapterResponse> WeakChapters { get; set; } = new();

    /// <summary>Card gia sư cho slider — dùng lại đúng shape của /api/tutors/recommend.</summary>
    public List<TutorRecommendItem> Tutors { get; set; } = new();

    /// <summary>
    /// Đánh giá đã bấm cho từng gia sư trong PHIÊN này: {tutorId -> 1 | -1}.
    /// </summary>
    public Dictionary<string, int> MyTutorVotes { get; set; } = new();

    public string SignalVersion { get; set; } = "";

    /// <summary>Định danh lượt gợi ý này, để gắn vào vote like/dislike card gia sư.</summary>
    public Guid SuggestionId { get; set; }

    /// <summary>
    /// Học sinh có đang bật nhận gợi ý không. Trả về kể cả khi ShouldShow=false để toggle
    /// trong menu tài khoản biết trạng thái mà không cần thêm một request riêng.
    /// </summary>
    public bool? PreferenceEnabled { get; set; }
}

/// <summary>Cấu hình gợi ý gia sư hiển thị trong CMS.</summary>
public class TutorSuggestionSettingsResponse
{
    public bool Enabled { get; set; }
    public int MinChapterCount { get; set; }
    public float MinConfidence { get; set; }
    public int TopK { get; set; }
}

/// <summary>Một chương học sinh hỏi nhiều lần trong phiên.</summary>
public class WeakChapterResponse
{
    public string Slug { get; set; } = "";

    /// <summary>Tên hiển thị lấy từ bảng chapters; nếu slug lạ thì suy từ chính slug.</summary>
    public string Name { get; set; } = "";

    /// <summary>Số bài đã hỏi về chương này trong phiên.</summary>
    public int Count { get; set; }

    public int? SubjectId { get; set; }

    public int? GradeLevelId { get; set; }
}
