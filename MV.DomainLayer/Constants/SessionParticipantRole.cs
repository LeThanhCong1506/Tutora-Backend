namespace MV.DomainLayer.Constants;

/// <summary>Role a user holds inside a live session, as stored in <c>session_participants.role</c>.</summary>
public static class SessionParticipantRole
{
    public const string Tutor = "tutor";
    public const string Student = "student";
    public const string Parent = "parent";
    public const string Recorder = "recorder";
    public const string Unknown = "unknown";
}
