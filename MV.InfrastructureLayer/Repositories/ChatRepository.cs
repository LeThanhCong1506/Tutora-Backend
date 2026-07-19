using Microsoft.EntityFrameworkCore;
using MV.DomainLayer.Entities;
using MV.InfrastructureLayer.DBContext;
using MV.ApplicationLayer.RepositoryInterfaces;
using MV.DomainLayer.Constants;

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

    public Task<bool> IsChannelParticipantAsync(int channelId, string userId)
        => context.Chatchannels
            .AsNoTracking()
            .AnyAsync(c =>
                c.Channelid == channelId &&
                (c.Parentid == userId || c.Tutorid == userId || c.Studentid == userId));

    public Task<bool> AreActiveChatPartnersAsync(string userId, string targetUserId)
        => context.Chatchannels
            .AsNoTracking()
            .AnyAsync(c =>
                c.Status == ChatChannelStatus.Active &&
                (c.Parentid == userId || c.Tutorid == userId || c.Studentid == userId) &&
                (c.Parentid == targetUserId || c.Tutorid == targetUserId || c.Studentid == targetUserId));

    public async Task<List<string>> GetAuthorizedPresenceUserIdsAsync(
        string requesterUserId,
        IReadOnlyCollection<string> requestedUserIds)
    {
        var requested = requestedUserIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (requested.Length == 0)
            return [];

        var activeChannels = context.Chatchannels
            .AsNoTracking()
            .Where(c =>
                c.Status == ChatChannelStatus.Active &&
                (c.Parentid == requesterUserId ||
                 c.Tutorid == requesterUserId ||
                 c.Studentid == requesterUserId));

        var parentIds = activeChannels
            .Where(c =>
                c.Parentid != null &&
                c.Parentid != requesterUserId &&
                requested.Contains(c.Parentid))
            .Select(c => c.Parentid!);
        var tutorIds = activeChannels
            .Where(c =>
                c.Tutorid != null &&
                c.Tutorid != requesterUserId &&
                requested.Contains(c.Tutorid))
            .Select(c => c.Tutorid!);
        var studentIds = activeChannels
            .Where(c =>
                c.Studentid != null &&
                c.Studentid != requesterUserId &&
                requested.Contains(c.Studentid))
            .Select(c => c.Studentid!);

        var authorizedPartners = await parentIds
            .Concat(tutorIds)
            .Concat(studentIds)
            .Distinct()
            .ToListAsync();

        if (requested.Contains(requesterUserId) &&
            !authorizedPartners.Contains(requesterUserId, StringComparer.Ordinal))
        {
            authorizedPartners.Add(requesterUserId);
        }

        return authorizedPartners;
    }

    public async Task<List<string>> GetChatPartnerUserIdsAsync(string userId)
    {
        var activeChannels = context.Chatchannels
            .AsNoTracking()
            .Where(c =>
                c.Status == ChatChannelStatus.Active &&
                (c.Parentid == userId || c.Tutorid == userId || c.Studentid == userId));

        var parentIds = activeChannels
            .Where(c => c.Parentid != null && c.Parentid != userId)
            .Select(c => c.Parentid!);
        var tutorIds = activeChannels
            .Where(c => c.Tutorid != null && c.Tutorid != userId)
            .Select(c => c.Tutorid!);
        var studentIds = activeChannels
            .Where(c => c.Studentid != null && c.Studentid != userId)
            .Select(c => c.Studentid!);

        return await parentIds
            .Concat(tutorIds)
            .Concat(studentIds)
            .Distinct()
            .ToListAsync();
    }

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
