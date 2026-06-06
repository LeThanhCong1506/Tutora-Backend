namespace MV.DomainLayer.Constants;

/// <summary>
/// Teaching mode constants. All values are lowercase to match the DB column and normalized input.
/// Input should always be normalized with .ToLowerInvariant() before comparison.
/// </summary>
public static class TeachingMode
{
    public const string Online  = "online";
    public const string Offline = "offline";
    public const string Hybrid  = "hybrid";
    public const string Both    = "both"; // Legacy alias used in some search filters

    public static readonly string[] All = [Online, Offline, Hybrid];
    public static readonly string[] Valid = [Online, Offline, Hybrid]; // for booking validation
}
