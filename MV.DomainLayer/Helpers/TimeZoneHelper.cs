namespace MV.DomainLayer.Helpers;

/// <summary>
/// Helper class for UTC-based datetime operations.
/// CONTRACT: All DB writes use UtcNow. All DB reads return UTC — FE handles display conversion.
/// </summary>
public static class TimeZoneHelper
{
    /// <summary>
    /// Current UTC time for database storage.
    /// </summary>
    public static DateTime UtcNow => DateTime.UtcNow;

    /// <summary>
    /// Gets a TimeZoneInfo from an IANA or Windows timezone ID. Returns UTC on invalid/empty input.
    /// </summary>
    public static TimeZoneInfo GetTimeZoneInfo(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
            return TimeZoneInfo.Utc;

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            if (timeZoneId.Equals("Asia/Ho_Chi_Minh", StringComparison.OrdinalIgnoreCase))
            {
                return OperatingSystem.IsWindows()
                    ? TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time")
                    : TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");
            }
            return TimeZoneInfo.Utc;
        }
        catch
        {
            return TimeZoneInfo.Utc;
        }
    }

    /// <summary>
    /// Converts a local DateTime from a specific timezone to UTC.
    /// Strips Kind before converting to avoid double-conversion.
    /// </summary>
    public static DateTime ToUtc(DateTime localDt, string? timeZoneId)
    {
        var tz = GetTimeZoneInfo(timeZoneId);
        var unspecified = DateTime.SpecifyKind(localDt, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(unspecified, tz);
    }
}
