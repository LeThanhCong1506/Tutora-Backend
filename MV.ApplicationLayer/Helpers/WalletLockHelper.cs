using Microsoft.EntityFrameworkCore;
using MV.ApplicationLayer.Interfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.Entities;

namespace MV.ApplicationLayer.Helpers;

/// <summary>
/// Wallet helpers for financial flows that already run inside a database transaction.
/// </summary>
public static class WalletLockHelper
{
    public static async Task<Wallet> GetOrCreateForUpdateAsync(
        IAppDbContext db,
        string userId,
        DateTime now,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("User id is required to lock a wallet.", nameof(userId));

        // Handles legacy users without wallets and is safe when another transaction creates it first.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO wallets (user_id, balance, frozen_balance, last_updated)
            VALUES ({userId}, {0m}, {0m}, {now})
            ON CONFLICT (user_id) DO NOTHING
            """, ct);

        return await db.Wallets
            .FromSqlRaw(SqlQueries.LockWalletByUserId, userId)
            .SingleAsync(ct);
    }

    public static async Task<Wallet> GetRequiredForUpdateAsync(
        IAppDbContext db,
        string userId,
        CancellationToken ct = default)
    {
        var wallet = await db.Wallets
            .FromSqlRaw(SqlQueries.LockWalletByUserId, userId)
            .SingleOrDefaultAsync(ct);

        return wallet ?? throw new InvalidOperationException($"Wallet for user '{userId}' does not exist.");
    }
}
