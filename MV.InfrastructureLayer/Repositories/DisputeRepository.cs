using Microsoft.EntityFrameworkCore;
using MV.ApplicationLayer.Helpers;
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
            .Include(d => d.DisputeEvidences)
                .ThenInclude(e => e.UploadedbyNavigation)
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
        // Chatchannel.Bookingid không bao giờ được ghi khi tạo kênh chat thật (ChatService tạo/tra
        // kênh theo cặp gia sư + phụ huynh/học sinh, xem GetOrCreateChannelForBookingAsync) — join
        // thẳng theo Bookingid ở đây sẽ luôn ra null. Phải tự resolve cùng cặp định danh đó.
        var booking = await context.Bookings
            .AsNoTracking()
            .Include(b => b.Student)
            .FirstOrDefaultAsync(b => b.Bookingid == bookingId);
        if (booking?.Tutorid == null) return null;

        var (payerId, isStudent) = BookingPayerResolver.Resolve(booking);
        if (string.IsNullOrWhiteSpace(payerId)) return null;

        var channel = await context.Chatchannels
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Tutorid == booking.Tutorid
                && (isStudent ? c.Studentid == payerId : c.Parentid == payerId));
        return channel?.Channelid;
    }

    // Admin xem lại toàn bộ chat để điều tra tranh chấp — không phải giao diện chat cuộn vô hạn
    // của tutor/student/parent, nên không cắt bớt như GetMessagesPagedAsync. limit chỉ để chặn
    // trường hợp cực đoan, không phải giới hạn hiển thị bình thường.
    public Task<List<Chatmessage>> GetChannelMessagesAsync(int channelId, int limit = 2000)
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
