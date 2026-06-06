namespace MV.DomainLayer.Constants;

/// <summary>
/// Warning severity levels issued by Admin.
/// Thấp (1) / Trung bình (2) → auto-suspend after 3 accumulated warnings in 30 days.
/// Cao (3)                   → auto-suspend immediately on first occurrence.
/// </summary>
public static class WarningLevel
{
    /// <summary>Thấp — minor infraction</summary>
    public const int Low = 1;

    /// <summary>Trung bình — moderate infraction</summary>
    public const int Medium = 2;

    /// <summary>Cao — severe infraction, triggers immediate suspension</summary>
    public const int High = 3;

    // Thresholds
    /// <summary>Number of Low/Medium warnings within 30 days before auto-suspend is triggered.</summary>
    public const int LowMediumSuspendThreshold = 3;

    /// <summary>Number of High warnings required to trigger auto-suspend (always 1).</summary>
    public const int HighSuspendThreshold = 1;
}
