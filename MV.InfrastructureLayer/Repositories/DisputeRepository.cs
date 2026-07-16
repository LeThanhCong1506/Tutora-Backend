using Microsoft.EntityFrameworkCore;
using MV.DomainLayer.Constants;
using MV.DomainLayer.Entities;
using MV.InfrastructureLayer.DBContext;
using MV.ApplicationLayer.RepositoryInterfaces;

namespace MV.InfrastructureLayer.Repositories;

public class DisputeRepository(AgoraDbContext context) : IDisputeRepository
{
    public IQueryable<Dispute> GetBaseQuery()
        => context.Disputes
            .AsNoTracking()
            .Include(d => d.ClassSession)
                .ThenInclude(l => l!.Tutor)
                    .ThenInclude(t => t!.Tutor)
            .Include(d => d.CreatedbyNavigation)
            .AsQueryable();

    public Task<Dispute?> GetDetailAsync(int disputeId)
        => context.Disputes
            .AsNoTracking()
            .Where(d => d.Disputeid == disputeId)
            .Include(d => d.ClassSession).ThenInclude(l => l!.Booking).ThenInclude(b => b!.Tutorsubjectgradeprice).ThenInclude(p => p!.Subject)
            .Include(d => d.ClassSession).ThenInclude(l => l!.Tutor).ThenInclude(t => t!.Tutor)
            .Include(d => d.CreatedbyNavigation)
            .Include(d => d.ResolvedbyNavigation)
            .FirstOrDefaultAsync();

    public Task<Dispute?> FindWithBookingAsync(int disputeId)
        => context.Disputes
            .Include(d => d.Booking)
            .FirstOrDefaultAsync(d => d.Disputeid == disputeId);

    public Task<Dispute?> FindWithClassSessionAsync(int disputeId)
        => context.Disputes
            .Include(d => d.ClassSession)
            .FirstOrDefaultAsync(d => d.Disputeid == disputeId);

    public Task<int> CountByStatusAsync(string status)
        => context.Disputes.CountAsync(d => d.Status == status);

    public Task<int> CountResolvedSinceAsync(DateTime since)
        => context.Disputes.CountAsync(d => d.Status == DisputeStatus.Resolved && d.Resolvedat >= since);

    public Task<decimal> SumRefundedSinceAsync(DateTime since)
        => context.Disputes
            .Where(d => d.Status == DisputeStatus.Resolved && d.Resolvedat >= since && d.Refundamount > 0)
            .SumAsync(d => d.Refundamount ?? 0);

    public Task<int> CountWarningsByTutorAsync(string tutorId)
        => context.Userwarnings.CountAsync(w => w.Userid == tutorId);

    public async Task<int?> GetChannelIdForBookingAsync(int bookingId)
    {
        var channel = await context.Chatchannels
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Bookingid == bookingId);
        return channel?.Channelid;
    }

    public Task<List<Chatmessage>> GetChannelMessagesAsync(int channelId, int limit = 100)
        => context.Chatmessages
            .AsNoTracking()
            .Where(m => m.Channelid == channelId)
            .OrderBy(m => m.Createdat)
            .Take(limit)
            .Include(m => m.Sender)
            .ToListAsync();

    public void Add(Dispute dispute)
        => context.Disputes.Add(dispute);

    public Task<int> SaveChangesAsync()
        => context.SaveChangesAsync();
}
