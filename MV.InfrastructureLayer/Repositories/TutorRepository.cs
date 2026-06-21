using System.Linq;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.Entities;
using MV.InfrastructureLayer.DBContext;
using MV.ApplicationLayer.RepositoryInterfaces;

namespace MV.InfrastructureLayer.Repositories
{
    public class TutorRepository : ITutorRepository
    {
        private readonly AgoraDbContext _context;

        public TutorRepository(AgoraDbContext context)
        {
            _context = context;
        }

        public async Task<Tutorprofile?> GetTutorProfileByIdAsync(string tutorId)
        {
            return await _context.Tutorprofiles
                                 .Include(t => t.Tutorsubjectgradeprices)
                                    .ThenInclude(p => p.Subject)
                                 .Include(t => t.Tutorsubjectgradeprices)
                                    .ThenInclude(p => p.Gradelevel)
                                 .FirstOrDefaultAsync(t => t.Tutorid == tutorId);
        }

        public async Task<Tutorprofile?> GetTutorProfileByUserIdAsync(string userId, CancellationToken cancellationToken = default)
        {
            return await _context.Tutorprofiles
                .Include(t => t.Tutor)
                .FirstOrDefaultAsync(t => t.Tutor.Userid == userId, cancellationToken);
        }

        public async Task<List<Tutorsubject>> GetTutorSubjectsByTutorIdAsync(string tutorId)
        {
            var prices = await _context.Tutorsubjectgradeprices
                .Include(ts => ts.Subject)
                .Include(ts => ts.Gradelevel)
                .Where(ts => ts.Tutorid == tutorId)
                .ToListAsync();

            return prices
                .GroupBy(p => p.Subjectid)
                .Select(g => new Tutorsubject
                {
                    Tutorid = tutorId,
                    Subjectid = g.Key,
                    Subject = g.First().Subject,
                    Gradelevels = JsonSerializer.Serialize(g.Select(p => p.Gradelevel.Gradename).Distinct().ToList())
                })
                .ToList();
        }

        public async Task<List<Tutorsubjectgradeprice>> GetTutorSubjectGradePricesAsync(string tutorId)
        {
            return await _context.Tutorsubjectgradeprices
                .Include(p => p.Subject)
                .Include(p => p.Gradelevel)
                .Where(p => p.Tutorid == tutorId)
                .OrderBy(p => p.Subjectid)
                .ThenBy(p => p.Gradelevel.Levelorder)
                .ToListAsync();
        }

        public void DeleteTutorSubjects(IEnumerable<Tutorsubject> subjects)
        {
            var tutorIds = subjects.Select(s => s.Tutorid).Where(id => id != null).Distinct().ToList();
            var prices = _context.Tutorsubjectgradeprices.Where(p => tutorIds.Contains(p.Tutorid)).ToList();
            if (!prices.Any()) return;

            var priceIds = prices.Select(p => p.Id).ToList();
            var referencedIds = _context.Bookings
                .Where(b => b.Tutorsubjectgradepriceid != null && priceIds.Contains(b.Tutorsubjectgradepriceid.Value))
                .Select(b => b.Tutorsubjectgradepriceid!.Value)
                .Distinct()
                .ToHashSet();

            var now = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
            foreach (var price in prices)
            {
                if (referencedIds.Contains(price.Id))
                {
                    price.Isactive = false;
                    price.Updatedat = now;
                }
                else
                {
                    _context.Tutorsubjectgradeprices.Remove(price);
                }
            }
        }

        public async Task CreateTutorSubjectsAsync(IEnumerable<Tutorsubject> subjects)
        {
            var defaultGrade = await _context.Gradelevels.OrderBy(g => g.Levelorder).FirstOrDefaultAsync();
            if (defaultGrade == null) return;

            var prices = subjects
                .Where(s => !string.IsNullOrWhiteSpace(s.Tutorid) && s.Subjectid.HasValue)
                .Select(s => new Tutorsubjectgradeprice
                {
                    Tutorid = s.Tutorid!,
                    Subjectid = s.Subjectid!.Value,
                    Gradelevelid = defaultGrade.Gradelevelid,
                    Priceperhour = 0,
                    Currency = "VND",
                    Isactive = false,
                    Createdat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow,
                    Updatedat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow
                });
            await _context.Tutorsubjectgradeprices.AddRangeAsync(prices);
        }

        public async Task ReplaceTutorSubjectGradePricesAsync(string tutorId, IEnumerable<Tutorsubjectgradeprice> prices)
        {
            var now = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
            var incomingList = prices.ToList();

            // Load all current prices for this tutor
            var existingPrices = await _context.Tutorsubjectgradeprices
                .Where(p => p.Tutorid == tutorId)
                .ToListAsync();

            var existingByKey = existingPrices.ToDictionary(p => (p.Subjectid, p.Gradelevelid));
            var incomingKeys = incomingList.Select(p => (p.Subjectid, p.Gradelevelid)).ToHashSet();

            // Upsert: update in-place or insert new
            foreach (var incoming in incomingList)
            {
                incoming.Tutorid = tutorId;
                incoming.Currency = string.IsNullOrWhiteSpace(incoming.Currency) ? "VND" : incoming.Currency;

                if (existingByKey.TryGetValue((incoming.Subjectid, incoming.Gradelevelid), out var existing))
                {
                    // UPDATE in-place — preserve Id so booking FKs stay valid
                    existing.Priceperhour = incoming.Priceperhour;
                    existing.Durationminutespersession = incoming.Durationminutespersession;
                    existing.Sessionsperweek = incoming.Sessionsperweek;
                    existing.Currency = incoming.Currency;
                    existing.Isactive = incoming.Isactive;
                    existing.Updatedat = now;
                }
                else
                {
                    // INSERT new
                    incoming.Createdat = now;
                    incoming.Updatedat = now;
                    await _context.Tutorsubjectgradeprices.AddAsync(incoming);
                }
            }

            // Handle removed prices (in DB but not in incoming request)
            var removedPrices = existingPrices.Where(p => !incomingKeys.Contains((p.Subjectid, p.Gradelevelid))).ToList();
            if (removedPrices.Any())
            {
                var removedIds = removedPrices.Select(p => p.Id).ToList();

                // Check which removed prices are still referenced by bookings
                var referencedIds = await _context.Bookings
                    .Where(b => b.Tutorsubjectgradepriceid != null && removedIds.Contains(b.Tutorsubjectgradepriceid.Value))
                    .Select(b => b.Tutorsubjectgradepriceid!.Value)
                    .Distinct()
                    .ToListAsync();

                foreach (var removed in removedPrices)
                {
                    if (referencedIds.Contains(removed.Id))
                    {
                        // Soft-delete: keep row so booking FK stays valid, just hide it
                        removed.Isactive = false;
                        removed.Updatedat = now;
                    }
                    else
                    {
                        _context.Tutorsubjectgradeprices.Remove(removed);
                    }
                }
            }
        }

        public async Task<Tutorsubjectgradeprice?> GetTutorSubjectGradePriceAsync(string tutorId, int subjectId, int gradeLevelId)
        {
            return await _context.Tutorsubjectgradeprices
                .Include(p => p.Subject)
                .Include(p => p.Gradelevel)
                .FirstOrDefaultAsync(p => p.Tutorid == tutorId 
                    && p.Subjectid == subjectId 
                    && p.Gradelevelid == gradeLevelId);
        }

        public async Task<bool> DeleteTutorSubjectGradePriceAsync(string tutorId, int subjectId, int gradeLevelId)
        {
            var price = await _context.Tutorsubjectgradeprices
                .FirstOrDefaultAsync(p => p.Tutorid == tutorId && p.Subjectid == subjectId && p.Gradelevelid == gradeLevelId);

            if (price == null)
                return false;

            var isReferenced = await _context.Bookings
                .AnyAsync(b => b.Tutorsubjectgradepriceid == price.Id);

            if (isReferenced)
            {
                price.Isactive = false;
                price.Updatedat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
            }
            else
            {
                _context.Tutorsubjectgradeprices.Remove(price);
            }

            return true;
        }

        public async Task AddTutorSubjectGradePriceAsync(Tutorsubjectgradeprice price)
        {
            var now = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
            price.Createdat ??= now;
            price.Updatedat = now;
            price.Currency = string.IsNullOrWhiteSpace(price.Currency) ? "VND" : price.Currency;
            
            await _context.Tutorsubjectgradeprices.AddAsync(price);
        }

        public async Task<List<int>> GetExistingSubjectIdsAsync(List<int> subjectIds)
        {
            return await _context.Subjects
                .Where(s => subjectIds.Contains(s.Subjectid))
                .Select(s => s.Subjectid)
                .ToListAsync();
        }

        public async Task<List<int>> GetExistingGradeLevelIdsAsync(List<int> gradeLevelIds)
        {
            return await _context.Gradelevels
                .Where(g => gradeLevelIds.Contains(g.Gradelevelid))
                .Select(g => g.Gradelevelid)
                .ToListAsync();
        }

        public async Task<List<Tutoravailability>> GetAvailabilitiesByTutorIdAsync(string tutorId)
        {
            return await _context.Tutoravailabilities
                .Where(a => a.Tutorid == tutorId)
                .ToListAsync();
        }

        #region Certificate Methods

        public async Task<List<Tutorcertificate>> GetCertificatesByTutorIdAsync(string tutorId)
        {
            return await _context.Tutorcertificates
                .Where(c => c.Tutorid == tutorId)
                .OrderByDescending(c => c.Createdat)
                .ToListAsync();
        }

        public async Task<Tutorcertificate?> GetCertificateByIdAsync(string certificateId)
        {
            return await _context.Tutorcertificates
                .FirstOrDefaultAsync(c => c.Certificateid == certificateId);
        }

        public async Task AddCertificateAsync(Tutorcertificate certificate)
        {
            await _context.Tutorcertificates.AddAsync(certificate);
        }

        public void DeleteCertificate(Tutorcertificate certificate)
        {
            _context.Tutorcertificates.Remove(certificate);
        }

        public Task<PagedList<Tutorcertificate>> GetAdminCertificatesAsync(CertificateParameters parameters)
        {
            var query = _context.Tutorcertificates
                .Include(c => c.Tutor)
                    .ThenInclude(p => p.Tutor)
                .AsNoTracking()
                .AsQueryable();

            // Status filter
            var status = parameters.Status?.Trim().ToLower();
            if (status != "all")
                query = query.Where(c => c.Verificationstatus == (status ?? CertificateStatus.PendingReview));

            // SearchTerm: tên hoặc email gia sư
            if (!string.IsNullOrWhiteSpace(parameters.SearchTerm))
            {
                var term = parameters.SearchTerm.Trim().ToLower();
                query = query.Where(c =>
                    (c.Tutor.Tutor.Fullname != null && c.Tutor.Tutor.Fullname.ToLower().Contains(term)) ||
                    (c.Tutor.Tutor.Email != null && c.Tutor.Tutor.Email.ToLower().Contains(term)));
            }

            // OrderBy
            query = parameters.OrderBy?.Trim().ToLower() switch
            {
                "createdat_asc"   => query.OrderBy(c => c.Createdat),
                "tutorname_asc"   => query.OrderBy(c => c.Tutor.Tutor.Fullname),
                "tutorname_desc"  => query.OrderByDescending(c => c.Tutor.Tutor.Fullname),
                _                 => query.OrderByDescending(c => c.Createdat)
            };

            return Task.FromResult(PagedList<Tutorcertificate>.ToPagedList(query, parameters.PageNumber, parameters.PageSize));
        }

        public async Task UpdateTutorProfileStatusAsync(Tutorprofile profile)
        {
            await _context.Tutorprofiles
                .Where(t => t.Tutorid == profile.Tutorid)
                .ForEachAsync(t =>
                {
                    t.Profilestatus = profile.Profilestatus;
                });
        }

        public Task UpdateTutorProfileAsync(Tutorprofile profile)
        {
            _context.Tutorprofiles.Update(profile);
            return Task.CompletedTask;
        }

        public async Task<List<Tutorpackage>> GetTutorPackagesAsync(string tutorId, bool includeInactive = false)
        {
            var query = _context.Tutorpackages
                .Include(c => c.Tutorpackagefixedslots)
                .Where(c => c.Tutorid == tutorId);

            if (!includeInactive)
            {
                query = query.Where(c => c.Isactive);
            }

            return await query
                .OrderByDescending(c => c.Createdat)
                .ToListAsync();
        }

        public async Task<Tutorpackage?> GetTutorPackageAsync(string tutorId, int packageId)
        {
            return await _context.Tutorpackages
                .Include(c => c.Tutorpackagefixedslots)
                .FirstOrDefaultAsync(c => c.Tutorid == tutorId && c.Packageid == packageId);
        }

        public async Task AddTutorPackageAsync(Tutorpackage package)
        {
            await _context.Tutorpackages.AddAsync(package);
        }

        #endregion

        #region Bank Verification Methods

        public async Task<List<BankChangeLog>> GetBankChangeLogsByTutorIdAsync(string tutorId, int limit = 10, CancellationToken cancellationToken = default)
        {
            return await _context.BankChangeLogs
                .Where(log => log.Tutorid == tutorId)
                .OrderByDescending(log => log.Changedat)
                .Take(limit)
                .ToListAsync(cancellationToken);
        }

        public async Task AddBankChangeLogAsync(BankChangeLog log, CancellationToken cancellationToken = default)
        {
            await _context.BankChangeLogs.AddAsync(log, cancellationToken);
        }

        public async Task<int> CountBankChangesInLastMonthAsync(string tutorId, CancellationToken cancellationToken = default)
        {
            var oneMonthAgo = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow.AddMonths(-1);
            return await _context.BankChangeLogs
                .Where(log => log.Tutorid == tutorId && log.Changedat >= oneMonthAgo)
                .CountAsync(cancellationToken);
        }

        public async Task<Tutorprofile?> GetTutorByVerificationCodeAsync(string? verificationCode, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(verificationCode))
                return null;

            return await _context.Tutorprofiles
                .FirstOrDefaultAsync(t => t.Bankverifycode == verificationCode, cancellationToken);
        }

        #endregion
    }
}
