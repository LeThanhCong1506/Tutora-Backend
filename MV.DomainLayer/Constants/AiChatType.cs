namespace MV.DomainLayer.Constants;

/// <summary>
/// Loại phiên chat AI (cột chat_sessions.session_type).
/// </summary>
public static class ChatSessionType
{
    public const string Homework = "homework";
    public const string TutorMatching = "tutor_matching";
}

/// <summary>
/// Vai trò của tin nhắn trong phiên chat AI (cột chat_histories.role).
/// </summary>
public static class ChatHistoryRole
{
    public const string User = "user";
    public const string Assistant = "assistant";
    public const string System = "system";
}
