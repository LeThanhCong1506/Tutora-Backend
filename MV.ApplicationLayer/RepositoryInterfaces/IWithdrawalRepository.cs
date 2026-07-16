using MV.DomainLayer.Entities;

namespace MV.ApplicationLayer.RepositoryInterfaces;

public interface IWithdrawalRepository
{
    /// <summary>Basic lookup — no navigation properties loaded.</summary>
    Task<Withdrawalrequest?> GetByIdAsync(int id, CancellationToken ct = default);

    /// <summary>Lookup with User and User.Tutorprofile navigation loaded.</summary>
    Task<Withdrawalrequest?> GetByIdWithUserAsync(int id, CancellationToken ct = default);

    /// <summary>Returns the base queryable (no tracking) for ad-hoc filtering in the service.</summary>
    IQueryable<Withdrawalrequest> GetBaseQuery();

    /// <summary>Counts withdrawal requests that are not finalized yet, including legacy approved rows.</summary>
    Task<int> CountPendingAsync(CancellationToken ct = default);

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
