using System;

namespace MV.DomainLayer.Entities;

/// <summary>
/// Vote like/dislike của người dùng cho 1 câu hỏi
/// </summary>
public partial class QuestionVote
{
    public Guid Id { get; set; }

    public Guid QuestionId { get; set; }

    public string UserId { get; set; } = null!;

    /// <summary>1 = like, -1 = dislike.</summary>
    public short Vote { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual QuestionBank? Question { get; set; }
}
