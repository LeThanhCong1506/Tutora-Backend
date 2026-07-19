using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Npgsql;

namespace MV.PresentationLayer.Migrations;

/// <summary>Runs only SQL embedded from migrations/managed.</summary>
public static class ManagedMigrationRunner
{
    private const string ResourceMarker = ".ManagedMigrations.";
    private const string AdvisoryLockName = "tutora-managed-migrations";

    public static async Task RunAsync(IConfiguration configuration, CancellationToken cancellationToken = default)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("ConnectionStrings:DefaultConnection is required to run migrations.");

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await ExecuteAsync(connection, "SELECT pg_advisory_lock(hashtext(@name));", cancellationToken,
            ("name", AdvisoryLockName));

        try
        {
            await ExecuteAsync(connection,
                """
                CREATE TABLE IF NOT EXISTS app_schema_migrations (
                    version VARCHAR(200) PRIMARY KEY,
                    checksum CHAR(64) NOT NULL,
                    applied_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
                );
                """, cancellationToken);

            var assembly = Assembly.GetExecutingAssembly();
            var resources = assembly.GetManifestResourceNames()
                .Where(name => name.Contains(ResourceMarker, StringComparison.Ordinal)
                    && name.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
                .OrderBy(name => name, StringComparer.Ordinal);

            foreach (var resourceName in resources)
            {
                var version = resourceName[(resourceName.IndexOf(ResourceMarker, StringComparison.Ordinal)
                    + ResourceMarker.Length)..];
                var script = await ReadResourceAsync(assembly, resourceName, cancellationToken);
                var checksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(script))).ToLowerInvariant();

                await using var checksumCommand = new NpgsqlCommand(
                    "SELECT checksum FROM app_schema_migrations WHERE version = @version;", connection);
                checksumCommand.Parameters.AddWithValue("version", version);
                var appliedChecksum = (string?)await checksumCommand.ExecuteScalarAsync(cancellationToken);
                if (appliedChecksum != null)
                {
                    if (!string.Equals(appliedChecksum.Trim(), checksum, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException($"Managed migration '{version}' changed after it was applied.");
                    continue;
                }

                await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
                try
                {
                    await using var migrationCommand = new NpgsqlCommand(script, connection, transaction)
                    {
                        CommandTimeout = 300
                    };
                    await migrationCommand.ExecuteNonQueryAsync(cancellationToken);

                    await using var journalCommand = new NpgsqlCommand(
                        "INSERT INTO app_schema_migrations(version, checksum) VALUES (@version, @checksum);",
                        connection, transaction);
                    journalCommand.Parameters.AddWithValue("version", version);
                    journalCommand.Parameters.AddWithValue("checksum", checksum);
                    await journalCommand.ExecuteNonQueryAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                }
                catch
                {
                    await transaction.RollbackAsync(cancellationToken);
                    throw;
                }
            }
        }
        finally
        {
            await ExecuteAsync(connection, "SELECT pg_advisory_unlock(hashtext(@name));", cancellationToken,
                ("name", AdvisoryLockName));
        }
    }

    private static async Task<string> ReadResourceAsync(Assembly assembly, string resourceName,
        CancellationToken cancellationToken)
    {
        await using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded migration '{resourceName}' was not found.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql,
        CancellationToken cancellationToken, params (string Name, object Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var parameter in parameters)
            command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
