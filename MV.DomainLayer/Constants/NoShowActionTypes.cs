namespace MV.DomainLayer.Constants;

/// <summary>
/// No-show action type constants. Values match the `noshowaction` column in the `classSessions` table.
/// </summary>
public static class NoShowActionTypes
{
    /// <summary>Full refund, session not counted.</summary>
    public const string FreeSession = "free_session";

    /// <summary>Schedule a makeup classSession.</summary>
    public const string Makeup = "makeup";

    /// <summary>Cancel booking and refund remaining sessions.</summary>
    public const string ChangeTutor = "change_tutor";

    public static readonly string[] All = { FreeSession, Makeup, ChangeTutor };
}
