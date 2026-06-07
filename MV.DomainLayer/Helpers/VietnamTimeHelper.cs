namespace MV.DomainLayer.Helpers;

/// <summary>
/// Helper class for Vietnam timezone conversions (UTC+7 / ICT).
/// Centralizes timezone logic to avoid duplication across services.
/// </summary>
[Obsolete("Sử dụng TimeZoneHelper với ITimezoneAccessor để hỗ trợ đa múi giờ thay vì hardcode UTC+7")]
public static class VietnamTimeHelper
{
    /// <summary>
    /// Vietnam timezone (UTC+7). Uses "SE Asia Standard Time" on Windows, "Asia/Ho_Chi_Minh" on Unix.
    /// </summary>
    public static readonly TimeZoneInfo Tz = TimeZoneInfo.FindSystemTimeZoneById(
        OperatingSystem.IsWindows() ? "SE Asia Standard Time" : "Asia/Ho_Chi_Minh");

    /// <summary>
    /// Converts a UTC DateTime to Vietnam time (UTC+7).
    /// </summary>
    /// <param name="dt">DateTime to convert. If Kind is not Utc, it will be treated as Utc.</param>
    /// <returns>DateTime in Vietnam timezone</returns>
    public static DateTime ToVietnamTime(DateTime dt)
    {
        var utc = DateTime.SpecifyKind(dt, DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTimeFromUtc(utc, Tz);
    }

    /// <summary>
    /// Converts a nullable UTC DateTime to Vietnam time (UTC+7).
    /// </summary>
    /// <param name="dt">Nullable DateTime to convert</param>
    /// <returns>DateTime in Vietnam timezone, or null if input is null</returns>
    public static DateTime? ToVietnamTime(DateTime? dt)
        => dt.HasValue ? ToVietnamTime(dt.Value) : null;

    /// <summary>
    /// Converts a Vietnam time DateTime to UTC.
    /// </summary>
    /// <param name="vnTime">DateTime in Vietnam timezone (will be treated as Unspecified)</param>
    /// <returns>DateTime in UTC</returns>
    public static DateTime ToUtc(DateTime vnTime)
    {
        var unspecified = DateTime.SpecifyKind(vnTime, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(unspecified, Tz);
    }

    /// <summary>
    /// Current UTC time for database storage and technical comparisons.
    /// </summary>
    public static DateTime UtcNow => DateTime.UtcNow;

    /// <summary>
    /// Gets the current time in Vietnam timezone.
    /// </summary>
    public static DateTime Now => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Tz);
}
