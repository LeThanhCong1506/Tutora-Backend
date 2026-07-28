using System;

namespace MV.DomainLayer.Entities;

/// <summary>
/// Đánh giá của học sinh cho một gia sư được gợi ý.
/// </summary>
public partial class TutorSuggestionVote
{
    public Guid Id { get; set; }

    /// <summary>Định danh lượt gợi ý.</summary>
    public Guid SuggestionId { get; set; }

    public Guid? SessionId { get; set; }

    public string TutorId { get; set; } = null!;

    public string UserId { get; set; } = null!;

    /// <summary>1 = thích, -1 = không thích.</summary>
    public short Vote { get; set; }

    /// <summary>Slug lý do, chỉ có khi dislike. Xem TutorSuggestionFeedbackReasons.</summary>
    public string? Reason { get; set; }

    public string? Detail { get; set; }

    /// <summary>
    /// Chương yếu tại thời điểm gợi ý — không có thì dislike không truy được nguyên nhân.
    /// </summary>
    public string? ChapterSlug { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual User? User { get; set; }
}
