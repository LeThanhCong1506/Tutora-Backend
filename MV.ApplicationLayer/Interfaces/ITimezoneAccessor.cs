namespace MV.ApplicationLayer.Interfaces;

/// <summary>
/// Provides access to the current user's timezone.
/// </summary>
public interface ITimezoneAccessor
{
    /// <summary>
    /// Gets the timezone ID for the current context (e.g., from HTTP headers).
    /// Returns "UTC" if not specified.
    /// </summary>
    string GetCurrentTimezone();
}
