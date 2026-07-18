using System.Data;
using System.Data.Common;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MV.InfrastructureLayer.DBContext;

/// <summary>
/// Explicit one-shot runner for the payment schema introduced outside EF
/// migrations. It is invoked by the deploy pipeline with --migrate-only; the
/// normal API startup only verifies the recorded schema signature.
/// </summary>
public static class PaymentSqlMigrationRunner
{
    private const long AdvisoryLockId = 2026071602;
    private const string HistoryTable = "public.schema_migrations";

    private static readonly MigrationResource[] Migrations =
    [
        new(
            "20260715b_drop_payment_transactions_topup_request_id",
            "MV.InfrastructureLayer.Migrations.V20260715b__drop_payment_transactions_topup_request_id.sql"),
        new(
            "20260716_payment_requests_and_transaction_reconciliation",
            "MV.InfrastructureLayer.Migrations.V20260716__payment_requests_and_transaction_reconciliation.sql"),
        new(
            "20260716b_backfill_payment_transaction_destination_accounts",
            "MV.InfrastructureLayer.Migrations.V20260716b__backfill_payment_transaction_destination_accounts.sql"),
        new(
            "20260717_rename_payment_transaction_channel_to_payment_method",
            "MV.InfrastructureLayer.Migrations.V20260717__rename_payment_transaction_channel_to_payment_method.sql"),
        new(
            "20260717b_scope_topup_requests_to_booking_shortfall",
            "MV.InfrastructureLayer.Migrations.V20260717b__scope_topup_requests_to_booking_shortfall.sql"),
        new(
            "20260718_manual_payout_claim_and_proof",
            "MV.InfrastructureLayer.Migrations.V20260718__manual_payout_claim_and_proof.sql")
    ];

    public static async Task ApplyPendingAsync(
        AgoraDbContext context,
        ILogger logger,
        CancellationToken ct = default)
    {
        var connection = context.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
            await connection.OpenAsync(ct);

        var acquired = false;
        try
        {
            acquired = await TryAcquireLockAsync(connection, ct);
            if (!acquired)
                throw new InvalidOperationException(
                    "Timed out waiting for the payment schema migration lock.");

            await EnsureBaselineSchemaAsync(connection, ct);
            await EnsureHistoryTableAsync(connection, ct);

            foreach (var migration in Migrations)
            {
                var sql = LoadResource(migration.ResourceName);
                var checksum = ComputeChecksum(sql);
                var appliedChecksum = await GetAppliedChecksumAsync(
                    connection,
                    migration.Version,
                    ct);

                if (appliedChecksum != null)
                {
                    if (!string.Equals(
                        appliedChecksum,
                        checksum,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException(
                            $"Migration {migration.Version} was already applied with a different checksum. "
                            + $"Applied={appliedChecksum}; expected={checksum}. "
                            + "Restore the immutable SQL artifact instead of rewriting migration history.");
                    }

                    logger.LogInformation(
                        "Payment schema migration {Version} already applied.",
                        migration.Version);
                    continue;
                }

                if (await HasPostconditionAsync(
                    connection,
                    migration.Version,
                    ct))
                {
                    await RecordAdoptedMigrationAsync(
                        connection,
                        migration.Version,
                        checksum,
                        ct);
                    logger.LogInformation(
                        "Adopted pre-existing payment schema migration {Version} after postcondition validation.",
                        migration.Version);
                    continue;
                }

                await ExecuteMigrationAsync(
                    connection,
                    migration,
                    sql,
                    checksum,
                    ct);
                logger.LogInformation(
                    "Applied payment schema migration {Version}.",
                    migration.Version);
            }

            await EnsureCurrentAsync(context, logger, ct);
        }
        finally
        {
            if (acquired)
            {
                await using var command = connection.CreateCommand();
                command.CommandText =
                    $"SELECT pg_advisory_unlock({AdvisoryLockId})";
                await command.ExecuteScalarAsync(CancellationToken.None);
            }

            if (openedHere)
                await connection.CloseAsync();
        }
    }

    public static async Task EnsureCurrentAsync(
        AgoraDbContext context,
        ILogger logger,
        CancellationToken ct = default)
    {
        var connection = context.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
            await connection.OpenAsync(ct);

        try
        {
            if (!await RelationExistsAsync(
                connection,
                "public.schema_migrations",
                ct))
            {
                throw MissingMigrationException();
            }

            foreach (var required in Migrations)
            {
                var sql = LoadResource(required.ResourceName);
                var expectedChecksum = ComputeChecksum(sql);
                var actualChecksum = await GetAppliedChecksumAsync(
                    connection,
                    required.Version,
                    ct);
                if (!string.Equals(
                    actualChecksum,
                    expectedChecksum,
                    StringComparison.OrdinalIgnoreCase)
                    || !await HasPostconditionAsync(
                        connection,
                        required.Version,
                        ct))
                {
                    throw MissingMigrationException();
                }
            }

            logger.LogInformation(
                "Payment schema is current at {Version}.",
                Migrations[^1].Version);
        }
        finally
        {
            if (openedHere)
                await connection.CloseAsync();
        }
    }

    private static async Task ExecuteMigrationAsync(
        DbConnection connection,
        MigrationResource migration,
        string sql,
        string checksum,
        CancellationToken ct)
    {
        await using var transaction =
            await connection.BeginTransactionAsync(ct);
        try
        {
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandTimeout = 300;
                command.CommandText = StripOuterTransaction(sql);
                await command.ExecuteNonQueryAsync(ct);
            }

            await InsertHistoryAsync(
                connection,
                transaction,
                migration.Version,
                checksum,
                ct);
            await transaction.CommitAsync(ct);
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }

    private static async Task RecordAdoptedMigrationAsync(
        DbConnection connection,
        string version,
        string checksum,
        CancellationToken ct)
    {
        await using var transaction =
            await connection.BeginTransactionAsync(ct);
        await InsertHistoryAsync(
            connection,
            transaction,
            version,
            checksum,
            ct);
        await transaction.CommitAsync(ct);
    }

    private static async Task InsertHistoryAsync(
        DbConnection connection,
        DbTransaction transaction,
        string version,
        string checksum,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            INSERT INTO {HistoryTable} (version, checksum, applied_at)
            VALUES (@version, @checksum, CURRENT_TIMESTAMP)
            ON CONFLICT (version) DO NOTHING
            """;
        AddParameter(command, "version", version);
        AddParameter(command, "checksum", checksum);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task EnsureHistoryTableAsync(
        DbConnection connection,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            CREATE TABLE IF NOT EXISTS {HistoryTable} (
                version VARCHAR(200) PRIMARY KEY,
                checksum VARCHAR(64) NOT NULL,
                applied_at TIMESTAMP WITHOUT TIME ZONE NOT NULL
                    DEFAULT CURRENT_TIMESTAMP
            )
            """;
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task EnsureBaselineSchemaAsync(
        DbConnection connection,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                to_regclass('public.bookings') IS NOT NULL
                AND to_regclass('public.bank_accounts') IS NOT NULL
                AND to_regclass('public.payment_transactions') IS NOT NULL
                AND to_regclass('public.system_alerts') IS NOT NULL
                AND NOT EXISTS (
                    SELECT 1
                    FROM information_schema.columns
                    WHERE table_schema = 'public'
                      AND table_name = 'tutor_profiles'
                      AND column_name IN (
                          'bank_name',
                          'bank_account_number',
                          'bank_account_name'
                      )
                )
            """;
        if (await command.ExecuteScalarAsync(ct) is not true)
        {
            throw new InvalidOperationException(
                "Payment migration baseline V20260715 is missing or partial. Apply V20260715__bank_accounts_and_payment_transactions.sql during a maintenance window first.");
        }
    }

    private static async Task<bool> HasPostconditionAsync(
        DbConnection connection,
        string version,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        if (version.StartsWith("20260718", StringComparison.Ordinal))
        {
            command.CommandText = """
                SELECT EXISTS (
                    SELECT 1
                    FROM information_schema.columns
                    WHERE table_schema = 'public'
                      AND table_name = 'withdrawal_requests'
                      AND column_name = 'claimed_by'
                )
                AND EXISTS (
                    SELECT 1
                    FROM information_schema.columns
                    WHERE table_schema = 'public'
                      AND table_name = 'withdrawal_requests'
                      AND column_name = 'claimed_at'
                )
                AND EXISTS (
                    SELECT 1
                    FROM information_schema.columns
                    WHERE table_schema = 'public'
                      AND table_name = 'withdrawal_requests'
                      AND column_name = 'rejection_reason'
                )
                AND EXISTS (
                    SELECT 1
                    FROM information_schema.columns
                    WHERE table_schema = 'public'
                      AND table_name = 'payment_transactions'
                      AND column_name = 'proof_image_path'
                )
                AND EXISTS (
                    SELECT 1
                    FROM pg_constraint
                    WHERE conname = 'withdrawal_requests_claimed_by_fkey'
                      AND conrelid = 'public.withdrawal_requests'::regclass
                )
                AND to_regclass(
                    'public.idx_withdrawal_requests_status_claimed_by'
                ) IS NOT NULL
                """;
        }
        else if (version.StartsWith("20260715b", StringComparison.Ordinal))
        {
            command.CommandText = """
                SELECT to_regclass('public.payment_transactions') IS NOT NULL
                  AND NOT EXISTS (
                      SELECT 1
                      FROM information_schema.columns
                      WHERE table_schema = 'public'
                        AND table_name = 'payment_transactions'
                        AND column_name = 'topup_request_id'
                  )
                """;
        }
        else if (version.StartsWith(
            "20260716b",
            StringComparison.Ordinal))
        {
            command.CommandText = """
                SELECT NOT EXISTS (
                    SELECT 1
                    FROM public.payment_transactions pt
                    INNER JOIN public.payment_requests pr
                        ON pr.payment_request_id = pt.payment_request_id
                    WHERE COALESCE(
                        to_jsonb(pt) ->> 'payment_method',
                        to_jsonb(pt) ->> 'channel'
                    ) = 'PayOS'
                      AND (
                          (NULLIF(BTRIM(pt.destination_account_bank_bin), '') IS NULL
                              AND NULLIF(BTRIM(pr.destination_bank_bin), '') IS NOT NULL)
                          OR (NULLIF(BTRIM(pt.destination_account_bank_name), '') IS NULL
                              AND NULLIF(BTRIM(pr.destination_bank_name), '') IS NOT NULL)
                          OR (NULLIF(BTRIM(pt.destination_account_number), '') IS NULL
                              AND NULLIF(BTRIM(pr.display_account_number), '') IS NOT NULL)
                          OR (NULLIF(BTRIM(pt.destination_account_name), '') IS NULL
                              AND NULLIF(BTRIM(pr.display_account_name), '') IS NOT NULL)
                      )
                )
                """;
        }
        else if (version.StartsWith(
            "20260717b",
            StringComparison.Ordinal))
        {
            command.CommandText = """
                SELECT EXISTS (
                    SELECT 1
                    FROM information_schema.columns
                    WHERE table_schema = 'public'
                      AND table_name = 'topup_requests'
                      AND column_name = 'booking_id'
                )
                AND EXISTS (
                    SELECT 1
                    FROM information_schema.columns
                    WHERE table_schema = 'public'
                      AND table_name = 'topup_requests'
                      AND column_name = 'payment_phase'
                )
                AND EXISTS (
                    SELECT 1
                    FROM pg_constraint
                    WHERE conname = 'topup_requests_booking_id_fkey'
                      AND conrelid = 'public.topup_requests'::regclass
                )
                AND EXISTS (
                    SELECT 1
                    FROM pg_constraint
                    WHERE conname = 'ck_topup_requests_booking_shortfall_scope'
                      AND conrelid = 'public.topup_requests'::regclass
                )
                AND to_regclass(
                    'public.idx_topup_requests_booking_phase_user_status'
                ) IS NOT NULL
                """;
        }
        else if (version.StartsWith(
            "20260717",
            StringComparison.Ordinal))
        {
            command.CommandText = """
                SELECT EXISTS (
                    SELECT 1
                    FROM information_schema.columns
                    WHERE table_schema = 'public'
                      AND table_name = 'payment_transactions'
                      AND column_name = 'payment_method'
                )
                AND NOT EXISTS (
                    SELECT 1
                    FROM information_schema.columns
                    WHERE table_schema = 'public'
                      AND table_name = 'payment_transactions'
                      AND column_name = 'channel'
                )
                AND to_regclass(
                    'public.uq_payment_transactions_payment_method_provider_transaction_id'
                ) IS NOT NULL
                AND to_regclass(
                    'public.uq_payment_transactions_payment_method_capture_fingerprint'
                ) IS NOT NULL
                """;
        }
        else
        {
            command.CommandText = """
                SELECT
                    to_regclass('public.payment_requests') IS NOT NULL
                    AND (
                        to_regclass('public.uq_payment_transactions_channel_capture_fingerprint') IS NOT NULL
                        OR to_regclass('public.uq_payment_transactions_payment_method_capture_fingerprint') IS NOT NULL
                    )
                    AND to_regclass('public.uq_payment_transactions_withdrawal_payout') IS NOT NULL
                    AND to_regclass('public.uq_payment_requests_provider_link') IS NOT NULL
                    AND to_regclass('public.uq_payment_requests_provider_order') IS NOT NULL
                    AND to_regclass('public.uq_payment_transactions_matched_request') IS NULL
                    AND 8 = (
                        SELECT COUNT(*)
                        FROM information_schema.columns
                        WHERE table_schema = 'public'
                          AND table_name = 'payment_transactions'
                          AND column_name IN (
                              'payment_request_id',
                              'capture_source',
                              'reconciliation_status',
                              'capture_fingerprint',
                              'destination_account_bank_bin',
                              'destination_virtual_account_number',
                              'destination_virtual_account_name',
                              'legacy_reconciliation_version'
                          )
                    )
                    AND EXISTS (
                        SELECT 1
                        FROM information_schema.columns
                        WHERE table_schema = 'public'
                          AND table_name = 'payment_requests'
                          AND column_name = 'legacy_reconciliation_version'
                    )
                    AND EXISTS (
                        SELECT 1
                        FROM information_schema.columns
                        WHERE table_schema = 'public'
                          AND table_name = 'payment_transactions'
                          AND column_name = 'webhook_payload'
                          AND data_type = 'text'
                    )
                    AND NOT EXISTS (
                        SELECT 1
                        FROM public.payment_transactions pt
                        WHERE COALESCE(
                            to_jsonb(pt) ->> 'payment_method',
                            to_jsonb(pt) ->> 'channel'
                        ) = 'PayOS'
                          AND to_jsonb(pt)
                                ->> 'legacy_reconciliation_version'
                                = 'V20260716'
                          AND jsonb_typeof(
                              (to_jsonb(pt) -> 'provider_payload')
                                  -> 'transactions'
                          ) = 'array'
                          AND to_jsonb(pt)
                                ->> 'reconciliation_status' NOT IN (
                              'Unexpected',
                              'Orphan'
                          )
                    )
                """;
        }

        return await command.ExecuteScalarAsync(ct) is true;
    }

    private static async Task<string?> GetAppliedChecksumAsync(
        DbConnection connection,
        string version,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT checksum
            FROM {HistoryTable}
            WHERE version = @version
            """;
        AddParameter(command, "version", version);
        return await command.ExecuteScalarAsync(ct) as string;
    }

    private static async Task<bool> RelationExistsAsync(
        DbConnection connection,
        string relation,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT to_regclass(@relation) IS NOT NULL";
        AddParameter(command, "relation", relation);
        return await command.ExecuteScalarAsync(ct) is true;
    }

    private static async Task<bool> TryAcquireLockAsync(
        DbConnection connection,
        CancellationToken ct)
    {
        for (var attempt = 0; attempt < 60; attempt++)
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                $"SELECT pg_try_advisory_lock({AdvisoryLockId})";
            if (await command.ExecuteScalarAsync(ct) is true)
                return true;
            await Task.Delay(TimeSpan.FromSeconds(1), ct);
        }

        return false;
    }

    private static string LoadResource(string resourceName)
    {
        var assembly = typeof(PaymentSqlMigrationRunner).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Embedded migration resource not found: {resourceName}");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static string StripOuterTransaction(string sql)
    {
        var lines = sql.Replace("\r\n", "\n").Split('\n').ToList();
        var beginIndex = lines.FindIndex(l =>
            string.Equals(l.Trim(), "BEGIN;", StringComparison.OrdinalIgnoreCase));
        var commitIndex = lines.FindLastIndex(l =>
            string.Equals(l.Trim(), "COMMIT;", StringComparison.OrdinalIgnoreCase));
        if (beginIndex >= 0)
            lines.RemoveAt(beginIndex);
        if (commitIndex >= 0)
        {
            if (beginIndex >= 0 && commitIndex > beginIndex)
                commitIndex--;
            lines.RemoveAt(commitIndex);
        }

        return string.Join('\n', lines);
    }

    private static string ComputeChecksum(string sql)
        => Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(sql)))
            .ToLowerInvariant();

    private static void AddParameter(
        DbCommand command,
        string name,
        object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static InvalidOperationException MissingMigrationException()
        => new(
            "Payment schema is not current. Run `dotnet MV.PresentationLayer.dll --migrate-only` before starting the API.");

    private sealed record MigrationResource(
        string Version,
        string ResourceName);
}
