namespace MV.ApplicationLayer.Helpers;

/// <summary>
/// The typed sentence that gates permanent account erasure.
/// </summary>
/// <remarks>
/// Naming both the operator and the target is the point: a phrase copied from a previous deletion,
/// or a dialog opened on the wrong row, fails to match. Kept separate from the service so the rule
/// can be tested on its own — it is the last thing standing between a click and an irreversible
/// delete, and the service around it needs a dozen injected dependencies to instantiate.
/// </remarks>
public static class UserPurgeConfirmation
{
    public static string Build(string? adminName, string? targetName)
        => $"Admin {Fallback(adminName, "Quản trị viên")} đồng ý xóa vĩnh viễn dữ liệu của người dùng {Fallback(targetName, "này")}";

    /// <summary>
    /// Tolerates stray or doubled spacing and casing, and nothing else — a near-miss on the names
    /// is exactly the mistake this is here to catch.
    /// </summary>
    public static bool Matches(string expected, string? typed)
    {
        if (string.IsNullOrWhiteSpace(typed)) return false;
        return string.Equals(Normalise(expected), Normalise(typed), StringComparison.OrdinalIgnoreCase);
    }

    private static string Fallback(string? value, string whenMissing)
        => string.IsNullOrWhiteSpace(value) ? whenMissing : value.Trim();

    private static string Normalise(string value)
        => string.Join(' ', value.Split(
            new[] { ' ', '\t', '\r', '\n' },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}
