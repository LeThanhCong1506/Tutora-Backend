namespace MV.DomainLayer.Helpers;

/// <summary>
/// Helper class for handling timezone conversions supporting multiple timezones (UTC+0 base).
/// Replaces VietnamTimeHelper.
/// </summary>
public static class TimeZoneHelper
{
    private static readonly TimeZoneInfo DefaultTimeZone = TimeZoneInfo.Utc;

    /// <summary>
    /// Lấy TimeZoneInfo từ IANA Timezone ID (ví dụ: Asia/Ho_Chi_Minh). 
    /// Nếu không hợp lệ hoặc rỗng, trả về UTC mặc định.
    /// Hệ thống có thể chuyển đổi chéo giữa IANA và Windows Timezone.
    /// </summary>
    public static TimeZoneInfo GetTimeZoneInfo(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return DefaultTimeZone;
        }

        try
        {
            // Trong .NET 6+, TimeZoneInfo.FindSystemTimeZoneById có hỗ trợ map giữa IANA và Windows
            // nếu package TimeZoneConverter được dùng, hoặc native nếu HĐH hỗ trợ.
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            // Fallback strategy for common timezones if native mapping fails
            if (timeZoneId.Equals("Asia/Ho_Chi_Minh", StringComparison.OrdinalIgnoreCase))
            {
                return OperatingSystem.IsWindows() 
                    ? TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time") 
                    : TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");
            }
            
            return DefaultTimeZone;
        }
        catch (Exception)
        {
            return DefaultTimeZone;
        }
    }

    /// <summary>
    /// Current UTC time for database storage. 
    /// Luôn luôn dùng method này thay vì DateTime.Now hay VietnamTimeHelper.Now khi lưu xuống DB.
    /// </summary>
    public static DateTime UtcNow => DateTime.UtcNow;

    /// <summary>
    /// Converts a UTC DateTime to the user's specific timezone.
    /// </summary>
    public static DateTime ToUserTime(DateTime utcDt, string? timeZoneId)
    {
        var tz = GetTimeZoneInfo(timeZoneId);
        var utc = DateTime.SpecifyKind(utcDt, DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTimeFromUtc(utc, tz);
    }

    /// <summary>
    /// Converts a UTC DateTime to the current user's timezone based on TimezoneContext.
    /// </summary>
    public static DateTime ToUserTime(DateTime utcDt)
    {
        return ToUserTime(utcDt, TimezoneContext.CurrentTimezone);
    }

    /// <summary>
    /// Converts a nullable UTC DateTime to the user's specific timezone.
    /// </summary>
    public static DateTime? ToUserTime(DateTime? utcDt, string? timeZoneId)
    {
        if (!utcDt.HasValue) return null;
        return ToUserTime(utcDt.Value, timeZoneId);
    }

    /// <summary>
    /// Converts a nullable UTC DateTime to the current user's timezone based on TimezoneContext.
    /// </summary>
    public static DateTime? ToUserTime(DateTime? utcDt)
    {
        if (!utcDt.HasValue) return null;
        return ToUserTime(utcDt.Value, TimezoneContext.CurrentTimezone);
    }

    /// <summary>
    /// Converts a local time from a specific user timezone to UTC.
    /// </summary>
    public static DateTime ToUtc(DateTime localDt, string? timeZoneId)
    {
        var tz = GetTimeZoneInfo(timeZoneId);
        var unspecified = DateTime.SpecifyKind(localDt, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(unspecified, tz);
    }

    /// <summary>
    /// Converts a local time from the current user's timezone to UTC based on TimezoneContext.
    /// </summary>
    public static DateTime ToUtc(DateTime localDt)
    {
        return ToUtc(localDt, TimezoneContext.CurrentTimezone);
    }

    /// <summary>
    /// Converts a weekly recurring schedule (ISO DayOfWeek and TimeOnly) from the user's timezone to UTC.
    /// This is essential for storing availability slots consistently.
    /// </summary>
    public static (int UtcIsoDayOfWeek, TimeOnly UtcTime) ShiftToUtc(int localIsoDayOfWeek, TimeOnly localTime, string? timeZoneId = null)
    {
        // Use an arbitrary week where dates perfectly map to ISO DayOfWeek.
        // Jan 1, 2024 is a Monday (ISO 1).
        var baseDate = new DateTime(2024, 1, localIsoDayOfWeek, localTime.Hour, localTime.Minute, localTime.Second, DateTimeKind.Unspecified);
        
        var utcDate = ToUtc(baseDate, timeZoneId ?? TimezoneContext.CurrentTimezone);
        
        var utcIsoDayOfWeek = utcDate.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)utcDate.DayOfWeek;
        var utcTime = TimeOnly.FromDateTime(utcDate);

        return (utcIsoDayOfWeek, utcTime);
    }

    /// <summary>
    /// Converts a weekly recurring schedule (ISO DayOfWeek and TimeOnly) from UTC to the user's timezone.
    /// </summary>
    public static (int LocalIsoDayOfWeek, TimeOnly LocalTime) ShiftToUserTime(int utcIsoDayOfWeek, TimeOnly utcTime, string? timeZoneId = null)
    {
        // Use an arbitrary week where dates perfectly map to ISO DayOfWeek.
        // Jan 1, 2024 is a Monday (ISO 1).
        var baseDate = new DateTime(2024, 1, utcIsoDayOfWeek, utcTime.Hour, utcTime.Minute, utcTime.Second, DateTimeKind.Utc);
        
        var localDate = ToUserTime(baseDate, timeZoneId ?? TimezoneContext.CurrentTimezone);
        
        var localIsoDayOfWeek = localDate.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)localDate.DayOfWeek;
        var localTime = TimeOnly.FromDateTime(localDate);

        return (localIsoDayOfWeek, localTime);
    }
}
