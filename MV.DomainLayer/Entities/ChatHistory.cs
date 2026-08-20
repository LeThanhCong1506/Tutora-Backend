using System;

namespace MV.DomainLayer.Entities;

/// <summary>
/// Một tin nhắn trong phiên chat AI. Bảng: chat_histories.
/// role: 'user' | 'assistant' | 'system'.
/// Grade/RagUsed là cột thống kê nội bộ — KHÔNG trả ra API response.
/// </summary>
public partial class ChatHistory
{
    public Guid MessageId { get; set; }

    public Guid SessionId { get; set; }

    public string Role { get; set; } = null!;

    public string Content { get; set; } = null!;

    public string? ImageUrl { get; set; }

    public string? Grade { get; set; }

    public bool RagUsed { get; set; }

    /// <summary>Similarity bài mẫu khớp nhất trong bank. NULL = không trúng.</summary>
    public float? RagSimilarity { get; set; }

    public Guid? RagQuestionId { get; set; }

    /// <summary>Code kiểm tra đáp số: true/false/NULL (không chạy được).</summary>
    public bool? AnswerVerified { get; set; }

    public string? Metadata { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual ChatSession? Session { get; set; }
}
