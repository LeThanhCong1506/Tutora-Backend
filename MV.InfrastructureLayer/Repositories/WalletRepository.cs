using Microsoft.EntityFrameworkCore;
using MV.DomainLayer.Constants;
using MV.DomainLayer.Entities;
using MV.InfrastructureLayer.DBContext;
using MV.ApplicationLayer.RepositoryInterfaces;

namespace MV.InfrastructureLayer.Repositories;

public class WalletRepository(AgoraDbContext context) : IWalletRepository
{
    public Task<Wallet?> GetByUserIdAsNoTrackingAsync(string userId, CancellationToken ct = default)
        => context.Wallets.AsNoTracking().FirstOrDefaultAsync(w => w.Userid == userId, ct);

    public Task<Wallet?> GetByUserIdAsync(string userId, CancellationToken ct = default)
        => context.Wallets.FirstOrDefaultAsync(w => w.Userid == userId, ct);

    public Task<Wallet?> GetByUserIdForUpdateAsync(string userId, CancellationToken ct = default)
        => context.Wallets
            .FromSqlRaw(SqlQueries.LockWalletByUserId, userId)
            .FirstOrDefaultAsync(ct);

    public async Task<Wallet> GetOrCreateForUpdateAsync(string userId, CancellationToken ct = default)
    {
        var wallet = await context.Wallets
            .FromSqlRaw(SqlQueries.LockWalletByUserId, userId)
            .FirstOrDefaultAsync(ct);

        if (wallet != null) return wallet;

        wallet = new Wallet
        {
            Userid = userId,
            Balance = 0,
            Frozenbalance = 0,
            Lastupdated = MV.DomainLayer.Helpers.VietnamTimeHelper.Now
        };
        context.Wallets.Add(wallet);
        return wallet;
    }

    public void Add(Wallet wallet)
        => context.Wallets.Add(wallet);

    public void AddTransaction(Wallettransaction transaction)
        => context.Wallettransactions.Add(transaction);

    public Task<bool> HasTransactionByDescriptionAsync(string description, string referenceTable, CancellationToken ct = default)
        => context.Wallettransactions.AsNoTracking()
            .AnyAsync(w => w.Referencetable == referenceTable && w.Description == description, ct);

    public Task<int> SaveChangesAsync(CancellationToken ct = default)
        => context.SaveChangesAsync(ct);
}
