using Microsoft.EntityFrameworkCore;
using MV.DomainLayer.Constants;
using MV.DomainLayer.Entities;
using MV.InfrastructureLayer.DBContext;
using MV.ApplicationLayer.RepositoryInterfaces;

namespace MV.InfrastructureLayer.Repositories
{
    public class StudentRepository : IStudentRepository
    {
        private readonly AgoraDbContext _context;
        // Bao gồm cả DepositPaid/PendingRemainingPayment — trước đây thiếu 2 trạng thái này nên
        // học sinh đã đóng cọc/đang chờ thanh toán phần còn lại vẫn có thể bị xoá nhầm.
        private static readonly string[] ActiveBookingStatuses =
        [
            BookingStatus.PendingTutor,
            BookingStatus.PendingPayment,
            BookingStatus.Paid,
            BookingStatus.DepositPaid,
            BookingStatus.PendingRemainingPayment,
            BookingStatus.Ongoing
        ];

        public StudentRepository(AgoraDbContext context)
            => _context = context ?? throw new ArgumentNullException(nameof(context));

        public Task<List<Studentprofile>> GetByParentIdAsync(string parentId)
            => _context.Studentprofiles
                .Include(sp => sp.GradelevelNavigation)
                .Where(sp => sp.Parentid == parentId && sp.Deletedat == null)
                .AsNoTracking()
                .ToListAsync();

        public Task<Studentprofile?> GetByIdAndParentAsync(string studentId, string parentId)
            => _context.Studentprofiles
                .Include(sp => sp.GradelevelNavigation)
                .FirstOrDefaultAsync(sp => sp.Studentid == studentId && sp.Parentid == parentId && sp.Deletedat == null);

        public Task<Studentprofile?> FindByStudentCodeAsync(string code)
            => _context.Studentprofiles
                .Include(sp => sp.GradelevelNavigation)
                .FirstOrDefaultAsync(sp => sp.Studentcode == code && sp.Deletedat == null);

        public Task<int> CountByParentIdAsync(string parentId)
            => _context.Studentprofiles
                .CountAsync(sp => sp.Parentid == parentId && sp.Deletedat == null);

        public async Task<int> GetMaxSeqByParentIdAsync(string parentId, string prefix)
        {
            var seqs = await _context.Studentprofiles
                .IgnoreQueryFilters()
                .Where(sp => sp.Parentid == parentId && sp.Studentid.StartsWith(prefix))
                .Select(sp => sp.Studentid.Substring(prefix.Length))
                .ToListAsync();
            return seqs.Count == 0 ? 0 : seqs.Select(s => int.TryParse(s, out var seq) ? seq : 0).Max();
        }

        public async Task CreateAsync(Studentprofile student)
            => await _context.Studentprofiles.AddAsync(student);

        public void Update(Studentprofile student)
            => _context.Studentprofiles.Update(student);

        public void SoftDelete(Studentprofile student)
        {
            student.Deletedat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
            _context.Update(student);
        }

        public Task<bool> HasActiveBookingAsync(string studentId)
            => _context.Bookings
                .AnyAsync(b => b.Studentid == studentId && ActiveBookingStatuses.Contains(b.Status!));

        public Task<bool> HasOpenDisputeAsync(string studentId)
            => _context.Disputes
                .AnyAsync(d => d.Booking!.Studentid == studentId
                    && d.Status != DisputeStatus.Resolved
                    && d.Status != DisputeStatus.Closed);

        public async Task<bool> IsStudentCodeUniqueAsync(string code)
        {
            return !await _context.Studentprofiles
                .AnyAsync(sp => sp.Studentcode == code && sp.Deletedat == null);
        }

        public Task<bool> IsStudentIdUniqueAsync(string id)
            => _context.Studentprofiles
                .AnyAsync(sp => sp.Studentid == id && sp.Deletedat == null);

        public async Task<string> GenerateUniqueStudentIdAsync()
        {
            const int maxAttempts = 10;
            for (var i = 0; i < maxAttempts; i++)
            {
                var candidate = $"STU-{Guid.NewGuid().ToString("N")[..10].ToUpperInvariant()}";
                var exists = await _context.Studentprofiles
                    .IgnoreQueryFilters()
                    .AnyAsync(sp => sp.Studentid == candidate);
                if (!exists) return candidate;
            }
            throw new InvalidOperationException("Không thể generate Studentid duy nhất sau nhiều lần thử.");
        }

        public Task<List<string>> GetAllStudentCodesAsync()
            => _context.Studentprofiles
                .Where(sp => sp.Studentcode != null && sp.Deletedat == null)
                .Select(sp => sp.Studentcode!)
                .ToListAsync();

        public Task<List<Studentprofile>> GetByLinkedUserIdAsync(string linkedUserId)
            => _context.Studentprofiles
                .Include(sp => sp.GradelevelNavigation)
                .Where(sp => sp.Linkeduserid == linkedUserId && sp.Deletedat == null)
                .ToListAsync();

        public Task<Studentprofile?> FindByStudentOrLinkedUserAsync(string userId)
            => _context.Studentprofiles
                .Include(sp => sp.GradelevelNavigation)
                .FirstOrDefaultAsync(sp => (sp.Studentid == userId || sp.Linkeduserid == userId) && sp.Deletedat == null);

        public Task<List<string>> GetStudentIdsByParentIdAsync(string parentId)
            => _context.Studentprofiles
                .AsNoTracking()
                .Where(sp => sp.Parentid == parentId && sp.Deletedat == null)
                .Select(sp => sp.Studentid)
                .ToListAsync();

        public async Task<HashSet<string>> GetParentManagedUserIdsAsync(IEnumerable<string> userIds)
        {
            var ids = userIds.Distinct().ToList();
            if (ids.Count == 0) return [];

            var profiles = await _context.Studentprofiles
                .AsNoTracking()
                .Where(sp => sp.Parentid != null && sp.Deletedat == null &&
                             (ids.Contains(sp.Studentid) || (sp.Linkeduserid != null && ids.Contains(sp.Linkeduserid))))
                .Select(sp => new { sp.Studentid, sp.Linkeduserid })
                .ToListAsync();

            var result = new HashSet<string>();
            foreach (var p in profiles)
            {
                if (ids.Contains(p.Studentid)) result.Add(p.Studentid);
                if (p.Linkeduserid != null && ids.Contains(p.Linkeduserid)) result.Add(p.Linkeduserid);
            }
            return result;
        }
    }
}
