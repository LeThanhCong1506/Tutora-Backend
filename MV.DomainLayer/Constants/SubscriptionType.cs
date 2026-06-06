namespace MV.DomainLayer.Constants;

/// <summary>
/// Tutor subscription tier constants. Values match the `subscriptiontype` column in the `tutorprofiles` table.
/// </summary>
public static class SubscriptionType
{
    public const string Free      = "free";
    public const string Guided    = "guided";
    public const string Intensive = "intensive";
    public const string Elite     = "elite";

    public static readonly string[] All = { Free, Guided, Intensive, Elite };
}
