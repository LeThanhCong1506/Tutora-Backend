using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Npgsql;

[assembly: InternalsVisibleTo("MV.ApplicationLayer.Tests")]

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
                var executableScript = NormalizeScriptForExecution(script);

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
                    await using var migrationCommand = new NpgsqlCommand(executableScript, connection, transaction)
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

    /// <summary>
    /// Removes one legacy transaction wrapper when, and only when, BEGIN and COMMIT are the first
    /// and last SQL statements. The caller must checksum and journal the original script; this
    /// normalized copy exists solely because the runner already owns the database transaction.
    /// </summary>
    internal static string NormalizeScriptForExecution(string script)
    {
        ArgumentNullException.ThrowIfNull(script);

        var statements = FindStatements(script);
        if (statements.Count < 2
            || !IsStandaloneTransactionStatement(script, statements[0], "BEGIN")
            || !IsStandaloneTransactionStatement(script, statements[^1], "COMMIT"))
        {
            return script;
        }

        var first = statements[0];
        var last = statements[^1];

        // Remove from right to left so the first statement's offsets stay valid. Everything else,
        // including comments, whitespace, and original line endings, remains byte-for-byte equal.
        var withoutCommit = script.Remove(last.Start, last.Length);
        return withoutCommit.Remove(first.Start, first.Length);
    }

    private static List<SqlStatementSpan> FindStatements(string script)
    {
        var statements = new List<SqlStatementSpan>();
        int? statementStart = null;

        for (var index = 0; index < script.Length;)
        {
            if (IsSqlWhitespace(script[index]))
            {
                index++;
                continue;
            }

            if (StartsWith(script, index, "--"))
            {
                index = SkipLineComment(script, index, script.Length);
                continue;
            }

            if (StartsWith(script, index, "/*"))
            {
                index = SkipBlockComment(script, index, script.Length);
                continue;
            }

            statementStart ??= index;

            if (script[index] is '\'' or '"')
            {
                index = SkipQuotedValue(script, index, script[index]);
                continue;
            }

            if (script[index] == '$'
                && TryReadDollarQuoteDelimiter(script, index, out var delimiter))
            {
                var closing = script.IndexOf(
                    delimiter,
                    index + delimiter.Length,
                    StringComparison.Ordinal);
                index = closing < 0 ? script.Length : closing + delimiter.Length;
                continue;
            }

            if (script[index] == ';')
            {
                statements.Add(new SqlStatementSpan(
                    statementStart.Value,
                    index + 1 - statementStart.Value));
                statementStart = null;
            }

            index++;
        }

        if (statementStart.HasValue)
            statements.Add(new SqlStatementSpan(statementStart.Value, script.Length - statementStart.Value));

        return statements;
    }

    private static bool IsStandaloneTransactionStatement(
        string script,
        SqlStatementSpan statement,
        string keyword)
    {
        var end = statement.Start + statement.Length;
        var index = SkipTrivia(script, statement.Start, end);

        if (index + keyword.Length > end
            || !script.AsSpan(index, keyword.Length).Equals(keyword, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        index += keyword.Length;
        if (index < end && IsIdentifierCharacter(script[index]))
            return false;

        index = SkipTrivia(script, index, end);
        if (index >= end || script[index] != ';')
            return false;

        index = SkipTrivia(script, index + 1, end);
        return index == end;
    }

    private static int SkipTrivia(string script, int index, int end)
    {
        while (index < end)
        {
            if (IsSqlWhitespace(script[index]))
            {
                index++;
            }
            else if (StartsWith(script, index, "--"))
            {
                index = SkipLineComment(script, index, end);
            }
            else if (StartsWith(script, index, "/*"))
            {
                index = SkipBlockComment(script, index, end);
            }
            else
            {
                break;
            }
        }

        return index;
    }

    private static int SkipLineComment(string script, int index, int end)
    {
        index += 2;
        while (index < end && script[index] is not '\r' and not '\n')
            index++;
        return index;
    }

    private static int SkipBlockComment(string script, int index, int end)
    {
        var depth = 1;
        index += 2;

        while (index < end && depth > 0)
        {
            if (StartsWith(script, index, "/*"))
            {
                depth++;
                index += 2;
            }
            else if (StartsWith(script, index, "*/"))
            {
                depth--;
                index += 2;
            }
            else
            {
                index++;
            }
        }

        return index;
    }

    private static int SkipQuotedValue(string script, int index, char quote)
    {
        index++;
        while (index < script.Length)
        {
            if (script[index] != quote)
            {
                index++;
                continue;
            }

            if (index + 1 < script.Length && script[index + 1] == quote)
            {
                index += 2;
                continue;
            }

            return index + 1;
        }

        return script.Length;
    }

    private static bool TryReadDollarQuoteDelimiter(string script, int index, out string delimiter)
    {
        var end = index + 1;
        while (end < script.Length && IsIdentifierCharacter(script[end]))
            end++;

        if (end >= script.Length || script[end] != '$')
        {
            delimiter = string.Empty;
            return false;
        }

        delimiter = script[index..(end + 1)];
        return true;
    }

    private static bool StartsWith(string value, int index, string expected)
        => index + expected.Length <= value.Length
           && value.AsSpan(index, expected.Length).SequenceEqual(expected);

    private static bool IsSqlWhitespace(char value)
        => char.IsWhiteSpace(value) || value == '\uFEFF';

    private static bool IsIdentifierCharacter(char value)
        => char.IsLetterOrDigit(value) || value == '_';

    private readonly record struct SqlStatementSpan(int Start, int Length);

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
