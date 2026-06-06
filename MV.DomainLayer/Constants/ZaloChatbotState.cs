namespace MV.DomainLayer.Constants;

/// <summary>
/// Zalo chatbot conversation state constants. Used by ZaloChatbotService state machine.
/// </summary>
public static class ZaloChatbotState
{
    public const string Idle       = "IDLE";
    public const string AskSubject = "ASK_SUBJECT";
    public const string AskGrade   = "ASK_GRADE";
    public const string AskArea    = "ASK_AREA";
}
