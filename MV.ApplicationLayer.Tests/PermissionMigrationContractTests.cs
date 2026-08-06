using MV.DomainLayer.Constants;
using Xunit;

namespace MV.ApplicationLayer.Tests;

public class PermissionMigrationContractTests
{
    [Fact]
    public void Migration_SeedsCompleteCatalogAndRetainsLegacyRollbackTable()
    {
        // Quét toàn bộ managed migrations chứ không riêng V20260716: migration đã apply thì
        // ManagedMigrationRunner khoá theo checksum, nên quyền thêm sau bắt buộc phải nằm ở
        // file mới. Hợp đồng thật là "mọi quyền được seed bởi một migration nào đó".
        var seeds = Directory
            .EnumerateFiles(RepoFile("migrations", "managed"), "*.sql")
            .Select(File.ReadAllText)
            .ToList();

        foreach (var permission in Permissions.All)
        {
            Assert.True(
                seeds.Any(seed => seed.Contains($"('{permission}'", StringComparison.Ordinal)),
                $"Permission '{permission}' chưa được seed trong migrations/managed. "
                + "Thêm một migration mới — đừng sửa file đã apply.");
        }

        var migration = File.ReadAllText(RepoFile(
            "migrations", "managed", "V20260716__staff_permission_groups.sql"));

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
