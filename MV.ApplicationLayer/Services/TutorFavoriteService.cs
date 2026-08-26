using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.Interfaces;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Entities;
using MV.DomainLayer.Helpers;

namespace MV.ApplicationLayer.Services;

/// <inheritdoc cref="ITutorFavoriteService"/>
public class TutorFavoriteService : ITutorFavoriteService
{
    private readonly IAppDbContext _context;
    private readonly ILogger<TutorFavoriteService> _logger;

    public TutorFavoriteService(IAppDbContext context, ILogger<TutorFavoriteService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<List<string>> GetFavoriteTutorIdsAsync(string userId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId)) return new List<string>();

        return await _context.TutorFavorites
            .AsNoTracking()
            .Where(f => f.Userid == userId)
            .OrderByDescending(f => f.Createdat)
            .Select(f => f.Tutorid)
            .ToListAsync(ct);
    }

    public async Task<List<TutorFavoriteResponse>> GetFavoritesAsync(string userId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId)) return new List<TutorFavoriteResponse>();

        var rows = await _context.TutorFavorites
            .AsNoTracking()
            .Where(f => f.Userid == userId)
            .OrderByDescending(f => f.Createdat)
            .Select(f => new
            {
                f.Tutorid,
                f.Createdat,
                Profile = f.TutorProfile,
                // Tutorprofile.Tutor is the tutor's User row (name, avatar, account status).
                Account = f.TutorProfile!.Tutor
            })
            .ToListAsync(ct);

        if (rows.Count == 0) return new List<TutorFavoriteResponse>();

        var tutorIds = rows.Select(r => r.Tutorid).ToList();

        // Same definition the tutor card uses, so a saved tutor does not show a different
        // session count here than in search results.
        var sessionCounts = await _context.ClassSessions
            .AsNoTracking()
            .Where(l => l.Tutorid != null && tutorIds.Contains(l.Tutorid)
                        && l.Status == ClassSessionStatus.Completed
                        && l.Issettled == true
                        && !l.Disputes.Any())
            .GroupBy(l => l.Tutorid!)
            .Select(g => new { TutorId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TutorId, x => x.Count, ct);

        var pricing = await _context.Tutorsubjectgradeprices
            .AsNoTracking()
            .Where(p => p.Isactive && tutorIds.Contains(p.Tutorid!))
            .Select(p => new { p.Tutorid, p.Priceperhour, SubjectName = p.Subject!.Subjectname })
            .ToListAsync(ct);

        return rows.Select(row =>
        {
            var profile = row.Profile;
            var prices = pricing.Where(p => p.Tutorid == row.Tutorid).ToList();

            return new TutorFavoriteResponse
            {
                TutorId = row.Tutorid,
                FullName = row.Account?.Fullname,
                AvatarUrl = row.Account?.Avatarurl,
                Headline = profile?.Headline,
                Education = profile?.Education,
                Degree = profile?.Degree,
                AverageRating = profile?.Averagerating,
                TotalReviews = profile?.Totalreviews,
                TotalClassSessions = sessionCounts.TryGetValue(row.Tutorid, out var count) ? count : 0,
                MinPricePerHour = prices.Count > 0 ? prices.Min(p => p.Priceperhour) : null,
                Subjects = prices.Select(p => p.SubjectName)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Select(name => name!)
                    .Distinct()
                    .ToList(),
                // Mirrors the marketplace gate in TutorSearchRepository. A saved tutor who has been
                // suspended or hidden stays listed but is flagged, rather than vanishing with no
                // explanation for the person who saved them.
                IsAvailable = row.Account?.Status == 1
                              && profile != null
                              && string.Equals(profile.Profilestatus, TutorProfileStatus.Active,
                                               StringComparison.OrdinalIgnoreCase)
                              && profile.Ispublic == true
                              && profile.Isacceptingbookings == true,
                SavedAt = row.Createdat
            };
        }).ToList();
    }

    public async Task<bool> ToggleFavoriteAsync(string userId, string tutorId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(tutorId))
            throw new ArgumentException("Thiếu thông tin người dùng hoặc gia sư.");

        var existing = await _context.TutorFavorites
            .FirstOrDefaultAsync(f => f.Userid == userId && f.Tutorid == tutorId, ct);

        if (existing != null)
        {
            _context.TutorFavorites.Remove(existing);
            await _context.SaveChangesAsync(ct);
            return false;
        }

        // Only a real tutor profile can be saved — the unique/foreign keys would reject anything
        // else anyway, but failing here gives the caller a usable message instead of a 500.
        var tutorExists = await _context.Tutorprofiles.AsNoTracking().AnyAsync(t => t.Tutorid == tutorId, ct);
        if (!tutorExists)
            throw new ArgumentException("Không tìm thấy gia sư này.");

        _context.TutorFavorites.Add(new TutorFavorite
        {
            Userid = userId,
            Tutorid = tutorId,
            Createdat = TimeZoneHelper.UtcNow
        });

        try
        {
            await _context.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            // Double-tap or two tabs racing: the unique constraint already holds the save we
            // wanted, so report it as saved rather than surfacing a database error.
            _logger.LogWarning(ex, "Duplicate favorite insert for user {UserId} / tutor {TutorId}", userId, tutorId);
            return true;
        }

        return true;
    }
}
