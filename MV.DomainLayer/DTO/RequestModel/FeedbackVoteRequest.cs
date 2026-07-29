using System.ComponentModel.DataAnnotations;

namespace MV.DomainLayer.DTO.RequestModel;

/// <summary>Đánh giá một câu trả lời của AI.</summary>
public class AiMessageVoteRequest
{
    /// <summary>1 = thích, -1 = không thích.</summary>
    [Range(-1, 1, ErrorMessage = "Giá trị đánh giá không hợp lệ.")]
    public short Vote { get; set; }

    /// <summary>Slug lý do (chỉ khi dislike). Xem AiMessageFeedbackReasons.</summary>
    [MaxLength(60)]
    public string? Reason { get; set; }

    /// <summary>Mô tả thêm, tuỳ chọn.</summary>
    [MaxLength(2000, ErrorMessage = "Nội dung góp ý quá dài.")]
    public string? Detail { get; set; }
}

/// <summary>Đánh giá một gia sư trong danh sách gợi ý.</summary>
public class TutorSuggestionVoteRequest
{
    /// <summary>Lượt gợi ý chứa gia sư này (lấy từ response gợi ý).</summary>
    [Required]
    public Guid SuggestionId { get; set; }

    [Required]
    [MaxLength(50)]
    public string TutorId { get; set; } = "";

    public Guid? SessionId { get; set; }

    [Range(-1, 1, ErrorMessage = "Giá trị đánh giá không hợp lệ.")]
    public short Vote { get; set; }

    /// <summary>Slug lý do (chỉ khi dislike). Xem TutorSuggestionFeedbackReasons.</summary>
    [MaxLength(60)]
    public string? Reason { get; set; }

    [MaxLength(2000, ErrorMessage = "Nội dung góp ý quá dài.")]
    public string? Detail { get; set; }

    /// <summary>Chương yếu lúc gợi ý — để truy nguyên nhân khi dislike.</summary>
    [MaxLength(120)]
    public string? ChapterSlug { get; set; }
}
