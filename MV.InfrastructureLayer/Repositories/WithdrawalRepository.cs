using Microsoft.EntityFrameworkCore;
using MV.DomainLayer.Constants;
using MV.DomainLayer.Entities;
using MV.InfrastructureLayer.DBContext;
using MV.ApplicationLayer.RepositoryInterfaces;

namespace MV.InfrastructureLayer.Repositories;

public class WithdrawalRepository(AgoraDbContext context) : IWithdrawalRepository
{
    public Task<Withdrawalrequest?> GetByIdAsync(int id, CancellationToken ct = default)
        => context.Withdrawalrequests.FirstOrDefaultAsync(w => w.Withdrawalid == id, ct);

    public Task<Withdrawalrequest?> GetByIdWithUserAsync(int id, CancellationToken ct = default)
        => context.Withdrawalrequests
            .Include(w => w.User)
                .ThenInclude(u => u!.Tutorprofile)
            .FirstOrDefaultAsync(w => w.Withdrawalid == id, ct);

    public IQueryable<Withdrawalrequest> GetBaseQuery()
        => context.Withdrawalrequests.AsNoTracking();

    public Task<int> CountPendingAsync(CancellationToken ct = default)
        => context.Withdrawalrequests.CountAsync(
            w => w.Status == WithdrawalStatus.PendingReview
                 || w.Status == WithdrawalStatus.Delayed
                 || w.Status == WithdrawalStatus.Pending
                 || w.Status == WithdrawalStatus.Approved, ct);

    public Task<int> SaveChangesAsync(CancellationToken ct = default)
        => context.SaveChangesAsync(ct);
}
