using System;

namespace MV.DomainLayer.Entities;

/// <summary>
/// Đánh giá của người dùng cho một câu trả lời của AI.
/// </summary>
public partial class AiMessageVote
{
    public Guid Id { get; set; }

    public Guid MessageId { get; set; }

    public string UserId { get; set; } = null!;

    /// <summary>1 = thích, -1 = không thích.</summary>
    public short Vote { get; set; }

    /// <summary>Slug lý do, chỉ có khi dislike. Xem AiFeedbackReasons.</summary>
    public string? Reason { get; set; }

    /// <summary>Mô tả thêm người dùng tự gõ (tuỳ chọn).</summary>
    public string? Detail { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual ChatHistory? Message { get; set; }

    public virtual User? User { get; set; }
}
