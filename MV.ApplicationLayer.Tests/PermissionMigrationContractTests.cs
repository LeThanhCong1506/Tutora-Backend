using MV.DomainLayer.Constants;
using Xunit;

namespace MV.ApplicationLayer.Tests;

public class PermissionMigrationContractTests
{
    [Fact]
    public void Migration_SeedsCompleteCatalogAndRetainsLegacyRollbackTable()
    {
        var migration = File.ReadAllText(RepoFile(
            "migrations", "managed", "V20260716__staff_permission_groups.sql"));

        foreach (var permission in Permissions.All)
            Assert.Contains($"('{permission}'", migration, StringComparison.Ordinal);

        Assert.Contains("string_agg(sp.permission_key", migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SELECT DISTINCT signature", migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("staff_permission_group_assignments", migration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DROP TABLE staff_permissions", migration, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rollback_ProjectsCurrentGroupRightsBackToDirectPermissions()
    {
        var rollback = File.ReadAllText(RepoFile(
            "migrations", "rollback", "V20260716__staff_permission_groups_to_direct_permissions.sql"));

        Assert.Contains("DELETE FROM staff_permissions", rollback, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("INSERT INTO staff_permissions", rollback, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("permission_group_permissions", rollback, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ON CONFLICT", rollback, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DROP TABLE staff_permissions", rollback, StringComparison.OrdinalIgnoreCase);
    }

    private static string RepoFile(params string[] parts)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Tutora-platform-backend.sln")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return Path.Combine(new[] { directory!.FullName }.Concat(parts).ToArray());
    }
}
