namespace MV.DomainLayer.DTO.ResponseModel;

/// <summary>One row in the Admin/Staff support inbox conversation list.</summary>
public class SupportThreadSummaryResponse
{
    public int SupportThreadId { get; set; }
    public string UserId { get; set; } = null!;
    public string? UserName { get; set; }
    public string? UserAvatarUrl { get; set; }
    /// <summary>Tutor | Parent | Student — see <see cref="MV.DomainLayer.Constants.UserRole"/>.</summary>
    public string? UserRole { get; set; }
    public string? LastMessage { get; set; }
    public DateTime? LastMessageAt { get; set; }
    /// <summary>True when the last message was sent by admin/staff — CMS prefixes the preview with "Bạn:".</summary>
    public bool LastMessageFromAdmin { get; set; }
    public int UnreadCount { get; set; }
}

/// <summary>A single message inside a support thread.</summary>
public class SupportMessageItemResponse
{
    public int SupportMessageId { get; set; }
    public string? SenderId { get; set; }
    /// <summary>"admin" or "user" — see <see cref="MV.DomainLayer.Constants.SupportSenderSide"/>.</summary>
    public string SenderSide { get; set; } = null!;
    public string? SenderName { get; set; }
    /// <summary>"text" or "image" — see <see cref="MV.DomainLayer.Constants.ChatMessageType"/>.</summary>
    public string MessageType { get; set; } = null!;
    /// <summary>The text content, or the image URL when <see cref="MessageType"/> is "image".</summary>
    public string Message { get; set; } = null!;
    public DateTime? CreatedAt { get; set; }
}

/// <summary>Full thread with the other participant's contact info and message history.</summary>
public class SupportThreadDetailResponse
{
    public int SupportThreadId { get; set; }
    public string UserId { get; set; } = null!;
    public string? UserName { get; set; }
    public string? UserAvatarUrl { get; set; }
    public string? UserRole { get; set; }
    public string? UserPhone { get; set; }
    public string? UserEmail { get; set; }
    public List<SupportMessageItemResponse> Messages { get; set; } = new();
}

/// <summary>Live SignalR payload ("supportMessageReceived") — carries the userId so a CMS inbox
/// with several threads open can tell which conversation the message belongs to.</summary>
public class SupportMessageBroadcast
{
    public string UserId { get; set; } = null!;
    public int SupportThreadId { get; set; }
    public SupportMessageItemResponse Message { get; set; } = null!;
}
