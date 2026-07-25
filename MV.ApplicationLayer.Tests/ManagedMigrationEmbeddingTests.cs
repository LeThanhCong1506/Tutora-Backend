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

    [Fact]
    public void NormalizeScriptForExecution_StripsOnlyOuterTransactionAndPreservesFormatting()
    {
        const string script =
            "-- immutable header\r\n"
            + "/* nested /* block */ comment */\r\n"
            + "BeGiN /* runner owns this transaction */ ;\r\n"
            + "SELECT 'COMMIT; is data';\r\n"
            + "DO $body$ BEGIN; PERFORM 1; COMMIT; END $body$;\r\n"
            + "CoMmIt;\r\n"
            + "-- immutable footer\r\n";

        const string expected =
            "-- immutable header\r\n"
            + "/* nested /* block */ comment */\r\n"
            + "\r\n"
            + "SELECT 'COMMIT; is data';\r\n"
            + "DO $body$ BEGIN; PERFORM 1; COMMIT; END $body$;\r\n"
            + "\r\n"
            + "-- immutable footer\r\n";

        Assert.Equal(expected, ManagedMigrationRunner.NormalizeScriptForExecution(script));
    }

    [Theory]
    [InlineData("SELECT 0;\nBEGIN;\nSELECT 1;\nCOMMIT;")]
    [InlineData("BEGIN;\nSELECT 1;\nCOMMIT;\nSELECT 2;")]
    [InlineData("BEGIN;\nSELECT 1;")]
    [InlineData("SELECT '-- BEGIN;';\nSELECT 'COMMIT;';")]
    public void NormalizeScriptForExecution_LeavesNonOuterOrIncompletePairsUntouched(string script)
    {
        Assert.Same(script, ManagedMigrationRunner.NormalizeScriptForExecution(script));
    }
}
