using System.Text.RegularExpressions;

namespace MV.ApplicationLayer.Helpers;

public enum DisputeIdentifierKind
{
    Any,
    Dispute,
    Booking,
    ClassSession
}

public readonly record struct DisputeIdentifierSearch(
    DisputeIdentifierKind Kind,
    int Id);

/// <summary>
/// Parses only complete, recognizable dispute identifiers. Numbers embedded in natural-language
/// reason searches stay plain text and are never reinterpreted as unrelated record ids.
/// </summary>
public static class DisputeSearchParser
{
    private static readonly Regex IdentifierPattern = new(
        @"^(?:(?<prefix>booking|buổi(?:\s+học)?|buoi(?:\s+hoc)?|session|khiếu\s+nại|khieu\s+nai|hồ\s+sơ|ho\s+so|dispute)\s*)?#?\s*(?<id>\d+)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static bool TryParseIdentifier(string? query, out DisputeIdentifierSearch identifier)
    {
        identifier = default;
        if (string.IsNullOrWhiteSpace(query)) return false;

        var match = IdentifierPattern.Match(query.Trim());
        if (!match.Success || !int.TryParse(match.Groups["id"].Value, out var id)) return false;

        var prefix = match.Groups["prefix"].Value.ToLowerInvariant();
        var kind = prefix switch
        {
            _ when prefix.StartsWith("booking", StringComparison.Ordinal) => DisputeIdentifierKind.Booking,
            _ when prefix.StartsWith("buổi", StringComparison.Ordinal)
                || prefix.StartsWith("buoi", StringComparison.Ordinal)
                || prefix.StartsWith("session", StringComparison.Ordinal) => DisputeIdentifierKind.ClassSession,
            "" => DisputeIdentifierKind.Any,
            _ => DisputeIdentifierKind.Dispute
        };

        identifier = new DisputeIdentifierSearch(kind, id);
        return true;
    }
}
