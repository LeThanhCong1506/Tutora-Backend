namespace MV.DomainLayer.Helpers;

/// <summary>
/// Maintains the current user's timezone across the async control flow using AsyncLocal.
/// This allows accessing the timezone anywhere without needing to inject an accessor service.
/// </summary>
public static class TimezoneContext
{
    private static readonly AsyncLocal<string?> _currentTimezone = new();

    public static string CurrentTimezone
    {
        get => _currentTimezone.Value ?? "UTC";
        set => _currentTimezone.Value = value;
    }
}
