namespace MV.DomainLayer.Constants;

/// <summary>
/// AI-classified dispute priority constants. Values match the `priority` column in the `disputes` table.
/// </summary>
public static class DisputePriority
{
    public const string Low = "low";
    public const string Medium = "medium";
    public const string High = "high";

    public static readonly string[] All = { Low, Medium, High };
}
