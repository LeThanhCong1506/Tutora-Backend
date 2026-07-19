using MV.PresentationLayer.Migrations;
using Xunit;

namespace MV.ApplicationLayer.Tests;

public class ManagedMigrationEmbeddingTests
{
    [Fact]
    public void StaffPermissionGroupMigration_IsEmbeddedForDeploymentRunner()
    {
        var resources = typeof(ManagedMigrationRunner).Assembly.GetManifestResourceNames();

        Assert.Contains(resources, resource =>
            resource.Contains(".ManagedMigrations.", StringComparison.Ordinal)
            && resource.EndsWith("V20260716__staff_permission_groups.sql", StringComparison.OrdinalIgnoreCase));
    }
}
