namespace MV.ApplicationLayer.Helpers;

/// <summary>
/// Canonical validation for identifiers supplied by live-session clients.
/// Redis and SignalR always operate on the compact "N" GUID representation.
/// </summary>
public static class LiveSessionDeviceInputValidator
{
    public const int MaxDeviceLabelLength = 120;

    public static bool TryNormalizeGuid(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (!Guid.TryParse(value, out var parsed) || parsed == Guid.Empty)
            return false;

        normalized = parsed.ToString("N");
        return true;
    }

    public static bool TryNormalizeDeviceLabel(string? value, out string normalized)
    {
        normalized = value?.Trim() ?? string.Empty;
        return normalized.Length is > 0 and <= MaxDeviceLabelLength
            && !normalized.Any(char.IsControl);
    }
}
