using System;
using System.Collections.Generic;

namespace MV.DomainLayer.Entities;

/// <summary>
/// Phiên hội thoại người dùng ↔ AI (homework | tutor_matching).
/// Bảng: chat_sessions. Tách hoàn toàn với chatchannels (chat người-người).
/// </summary>
public partial class ChatSession
{
    public Guid SessionId { get; set; }

    public string UserId { get; set; } = null!;

    public string SessionType { get; set; } = null!;

    /// <summary>Chỉ set khi SessionType = video_summary — buổi học mà phiên chat này đang bàn về.</summary>
    public int? ClassSessionId { get; set; }

    public string? Title { get; set; }

    public bool? IsActive { get; set; }

    public string? Metadata { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual User? User { get; set; }

    public virtual ICollection<ChatHistory> ChatHistories { get; set; } = new List<ChatHistory>();
}
