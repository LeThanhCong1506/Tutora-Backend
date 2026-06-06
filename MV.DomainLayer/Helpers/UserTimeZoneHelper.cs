namespace MV.DomainLayer.Helpers;

/// <summary>
/// Helper for user timezone conversion. Database timestamps remain UTC.
/// </summary>
public static class UserTimeZoneHelper
{
    public const string DefaultTimeZoneId = "Asia/Ho_Chi_Minh";
    public const string HeaderName = "X-Time-Zone";

    private static readonly IReadOnlyDictionary<string, string> WindowsFallbackIds =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Asia/Ho_Chi_Minh"] = "SE Asia Standard Time",
            ["Asia/Bangkok"] = "SE Asia Standard Time",
            ["Asia/Saigon"] = "SE Asia Standard Time",
            ["UTC"] = "UTC",
            ["Etc/UTC"] = "UTC"
        };

    /// <summary>
    /// Resolve a client-provided timezone id, falling back to Vietnam time.
    /// </summary>
    public static TimeZoneInfo Resolve(string? timeZoneId)
    {
        var candidate = string.IsNullOrWhiteSpace(timeZoneId)
            ? DefaultTimeZoneId
            : timeZoneId.Trim();

        if (TryFind(candidate, out var timeZone))
            return timeZone;

        return VietnamTimeHelper.Tz;
    }

    /// <summary>
    /// Convert a UTC DateTime into a user timezone.
    /// </summary>
    public static DateTime FromUtc(DateTime utcTime, string? timeZoneId)
    {
        var utc = DateTime.SpecifyKind(utcTime, DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTimeFromUtc(utc, Resolve(timeZoneId));
    }

    /// <summary>
    /// Convert a local DateTime in the user timezone into UTC.
    /// </summary>
    public static DateTime ToUtc(DateTime localTime, string? timeZoneId)
    {
        var unspecified = DateTime.SpecifyKind(localTime, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(unspecified, Resolve(timeZoneId));
    }

    private static bool TryFind(string timeZoneId, out TimeZoneInfo timeZone)
    {
        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
        }
        catch (InvalidTimeZoneException)
        {
        }

        if (OperatingSystem.IsWindows()
            && WindowsFallbackIds.TryGetValue(timeZoneId, out var windowsId))
        {
            try
            {
                timeZone = TimeZoneInfo.FindSystemTimeZoneById(windowsId);
                return true;
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        timeZone = TimeZoneInfo.Utc;
        return false;
    }
}
