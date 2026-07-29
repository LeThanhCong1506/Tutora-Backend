using Microsoft.EntityFrameworkCore;
using MV.DomainLayer.Entities;
using MV.InfrastructureLayer.DBContext;
using MV.ApplicationLayer.RepositoryInterfaces;

namespace MV.InfrastructureLayer.Repositories;

public class WarningRepository(AgoraDbContext context) : IWarningRepository
{
    // ── Userwarnings ──────────────────────────────────────────────────────────

    public void AddWarning(Userwarning warning)
        => context.Userwarnings.Add(warning);

    public Task<List<Userwarning>> GetRecentWarningsAsync(string userId, DateTime since)
        => context.Userwarnings
            .AsNoTracking()
            .Where(w => w.Userid == userId && w.Createdat >= since)
            .ToListAsync();

    public Task<List<Userwarning>> GetAllWarningsAsync(string userId)
        => context.Userwarnings
            .AsNoTracking()
            .Where(w => w.Userid == userId)
            .OrderByDescending(w => w.Createdat)
            .Include(w => w.IssuedbyNavigation)
            .ToListAsync();

    // ── Profilesuspensions ────────────────────────────────────────────────────

    public Task<bool> HasActiveSuspensionAsync(string userId)
        => context.Profilesuspensions
            .AnyAsync(s => s.Userid == userId && s.Isactive == true);

    public Task<int> CountRecentSuspensionsAsync(string userId, string suspensionType, DateTime since)
        => context.Profilesuspensions
            .CountAsync(s => s.Userid == userId &&
                             s.Suspensiontype == suspensionType &&
                             s.Startdate >= since);

    public Task<Profilesuspension?> GetActiveSuspensionAsync(string userId)
        => context.Profilesuspensions
            .AsNoTracking()
            .Where(s => s.Userid == userId && s.Isactive == true)
            .OrderByDescending(s => s.Startdate)
            .FirstOrDefaultAsync();

    public Task<List<Profilesuspension>> GetUserSuspensionsAsync(string userId)
        => context.Profilesuspensions
            .AsNoTracking()
            .Where(s => s.Userid == userId)
            .OrderByDescending(s => s.Startdate)
            .Include(s => s.CreatedbyNavigation)
            .ToListAsync();

    public Task<List<Profilesuspension>> GetExpiredActiveSuspensionsAsync(DateTime now)
        => context.Profilesuspensions
            .Where(s => s.Isactive == true && s.Enddate.HasValue && s.Enddate.Value <= now)
            .Include(s => s.User)
            .ToListAsync();

    public async Task<(IReadOnlyList<Profilesuspension> Items, int Total)> GetActiveSuspensionsPagedAsync(
        int page, int pageSize)
    {
        var q = context.Profilesuspensions
            .AsNoTracking()
            .Where(s => s.Isactive == true)
            .Include(s => s.User)
            .Include(s => s.CreatedbyNavigation)
            .OrderByDescending(s => s.Startdate);

        var total = await q.CountAsync();
        var items = await q.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return (items, total);
    }

    public void AddSuspension(Profilesuspension suspension)
        => context.Profilesuspensions.Add(suspension);

    // ── Helpers ───────────────────────────────────────────────────────────────

    public Task<Tutorprofile?> GetTutorProfileAsync(string userId)
        => context.Tutorprofiles.FirstOrDefaultAsync(t => t.Tutorid == userId);

    public Task<int> SaveChangesAsync()
        => context.SaveChangesAsync();
}
