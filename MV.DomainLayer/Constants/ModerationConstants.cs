namespace MV.DomainLayer.Constants;

/// <summary>
/// Content moderation constants for FPT AI moderation service.
/// </summary>
public static class ModerationConstants
{
    /// <summary>Moderation category constants.</summary>
    public static class Category
    {
        public const string Profanity       = "profanity";
        public const string SensitiveContent = "sensitive_content";
        public const string PersonalInfo    = "personal_info";
    }

    /// <summary>Moderation severity level constants.</summary>
    public static class Severity
    {
        public const string High   = "high";
        public const string Medium = "medium";
        public const string Low    = "low";
    }
}
