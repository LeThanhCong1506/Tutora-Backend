using Microsoft.EntityFrameworkCore;
using MV.DomainLayer.Entities;
using MV.InfrastructureLayer.DBContext;
using MV.ApplicationLayer.RepositoryInterfaces;

namespace MV.InfrastructureLayer.Repositories;

public class ChatRepository(AgoraDbContext context) : IChatRepository
{
    // ── Channels ──────────────────────────────────────────────────────────────

    public Task<Chatchannel?> FindChannelByIdAsync(int channelId)
        => context.Chatchannels
            .FirstOrDefaultAsync(c => c.Channelid == channelId);

    public Task<Chatchannel?> FindChannelByIdWithBookingAsync(int channelId)
        => context.Chatchannels
            .Include(c => c.Booking)
            .FirstOrDefaultAsync(c => c.Channelid == channelId);

    public Task<Chatchannel?> FindChannelByParticipantsAsync(
        string tutorId, string? parentId, string? studentId)
    {
        if (studentId != null)
            return context.Chatchannels
                .FirstOrDefaultAsync(c => c.Tutorid == tutorId && c.Studentid == studentId);

        return context.Chatchannels
            .FirstOrDefaultAsync(c => c.Tutorid == tutorId && c.Parentid == parentId);
    }

    public Task<List<Chatchannel>> GetChannelsByUserAsync(string userId)
        => context.Chatchannels
            .Include(c => c.Parent)
            .Include(c => c.Tutor)
            .Include(c => c.Student)
            .Include(c => c.Chatmessages.OrderByDescending(m => m.Createdat))
            .Where(c => c.Parentid == userId || c.Tutorid == userId || c.Studentid == userId)
            .OrderByDescending(c => c.Lastmessageat)
            .AsNoTracking()
            .ToListAsync();

    public void AddChannel(Chatchannel channel)
        => context.Chatchannels.Add(channel);

    public void UpdateChannel(Chatchannel channel)
        => context.Chatchannels.Update(channel);

    // ── Messages ──────────────────────────────────────────────────────────────

    public async Task<(IReadOnlyList<Chatmessage> Items, int Total)> GetMessagesPagedAsync(
        int channelId, int page, int pageSize, string? searchQuery = null)
    {
        var q = context.Chatmessages
            .Include(m => m.Sender)
            .Where(m => m.Channelid == channelId)
            .Where(m => string.IsNullOrWhiteSpace(searchQuery) || m.Content!.Contains(searchQuery))
            .OrderByDescending(m => m.Createdat)
            .AsNoTracking();

        var total = await q.CountAsync();
        var items = await q.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return (items, total);
    }

    public Task<List<Chatmessage>> GetUnreadMessagesAsync(int channelId, string senderId)
        => context.Chatmessages
            .Where(m => m.Channelid == channelId &&
                        m.Senderid != senderId &&
                        (m.Isread == null || m.Isread == false))
            .ToListAsync();

    public Task<int> GetUnreadTotalCountAsync(string userId)
        => context.Chatmessages
            .AsNoTracking()
            .Join(context.Chatchannels.AsNoTracking(),
                m => m.Channelid,
                c => c.Channelid,
                (m, c) => new { m, c })
            .CountAsync(x =>
                (x.c.Parentid == userId || x.c.Tutorid == userId || x.c.Studentid == userId) &&
                x.m.Senderid != userId &&
                (x.m.Isread == null || x.m.Isread == false));

    public void AddMessage(Chatmessage message)
        => context.Chatmessages.Add(message);

    public Task<int> SaveChangesAsync()
        => context.SaveChangesAsync();
}
