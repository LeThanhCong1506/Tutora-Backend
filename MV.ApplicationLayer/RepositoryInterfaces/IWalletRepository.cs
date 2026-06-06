using MV.DomainLayer.Entities;

namespace MV.ApplicationLayer.RepositoryInterfaces;

public interface IWalletRepository
{
    /// <summary>Returns the wallet for a user, or null. No tracking.</summary>
    Task<Wallet?> GetByUserIdAsNoTrackingAsync(string userId, CancellationToken ct = default);

    /// <summary>Returns the wallet with tracking so it can be mutated.</summary>
    Task<Wallet?> GetByUserIdAsync(string userId, CancellationToken ct = default);

    /// <summary>
    /// Returns the wallet with a PostgreSQL row-level lock (SELECT … FOR UPDATE).
    /// Must be called inside a transaction.
    /// </summary>
    Task<Wallet?> GetByUserIdForUpdateAsync(string userId, CancellationToken ct = default);

    /// <summary>
    /// Returns the wallet with a row-level lock, creating it if it doesn't exist yet.
    /// Must be called inside a transaction.
    /// </summary>
    Task<Wallet> GetOrCreateForUpdateAsync(string userId, CancellationToken ct = default);

    void Add(Wallet wallet);

    void AddTransaction(Wallettransaction transaction);

    /// <summary>Returns true if a wallet transaction with the given reference description already exists.</summary>
    Task<bool> HasTransactionByDescriptionAsync(string description, string referenceTable, CancellationToken ct = default);

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
