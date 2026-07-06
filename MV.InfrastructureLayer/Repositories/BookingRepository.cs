using Microsoft.EntityFrameworkCore;
using MV.DomainLayer.Constants;
using MV.DomainLayer.Entities;
using MV.InfrastructureLayer.DBContext;
using MV.ApplicationLayer.RepositoryInterfaces;

namespace MV.InfrastructureLayer.Repositories;

public class BookingRepository(AgoraDbContext context) : IBookingRepository
{
    public Task<Booking?> FindByIdAsync(int id)
        => context.Bookings.FindAsync(id).AsTask();

    public Task<Booking?> FindWithStudentAsync(int id)
        => context.Bookings
            .Include(b => b.Student)
            .FirstOrDefaultAsync(b => b.Bookingid == id);

    public Task<Booking?> FindWithRelationsAsync(int id)
        => context.Bookings
            .Include(b => b.Student).ThenInclude(s => s!.Linkeduser)
            .Include(b => b.Student).ThenInclude(s => s!.GradelevelNavigation)
            .Include(b => b.Tutor).ThenInclude(t => t!.Tutor)
            .Include(b => b.Tutorsubjectgradeprice).ThenInclude(p => p!.Subject)
            .Include(b => b.Tutorsubjectgradeprice).ThenInclude(p => p!.Gradelevel)
            .Include(b => b.Package)
            .Include(b => b.ClassSessions)
            .FirstOrDefaultAsync(b => b.Bookingid == id);

    public Task<Booking?> FindWithSubjectAsync(int id)
        => context.Bookings
            .Include(b => b.Tutorsubjectgradeprice).ThenInclude(p => p!.Subject)
            .Include(b => b.Tutorsubjectgradeprice).ThenInclude(p => p!.Gradelevel)
            .FirstOrDefaultAsync(b => b.Bookingid == id);

    public Task<Booking?> FindByIdForUserAsync(int id, string userId)
        => context.Bookings
            .Include(b => b.Student)
            .Include(b => b.Student).ThenInclude(s => s!.GradelevelNavigation)
            .Include(b => b.Tutor).ThenInclude(t => t!.Tutor)
            .Include(b => b.Tutorsubjectgradeprice).ThenInclude(p => p!.Subject)
            .Include(b => b.Tutorsubjectgradeprice).ThenInclude(p => p!.Gradelevel)
            .Include(b => b.Package)
            .Include(b => b.ClassSessions)
            .FirstOrDefaultAsync(b => b.Bookingid == id &&
                (b.Parentid == userId || b.Studentid == userId || b.Student!.Linkeduserid == userId || b.Tutorid == userId));

    public async Task<(IReadOnlyList<Booking> Items, int Total)> GetByParentIdPagedAsync(
        string parentId, int page, int pageSize, string? status)
    {
        var q = context.Bookings
            .AsNoTracking()
            .Include(b => b.Student)
            .Include(b => b.Student).ThenInclude(s => s!.GradelevelNavigation)
            .Include(b => b.Tutor).ThenInclude(t => t!.Tutor)
            .Include(b => b.Tutorsubjectgradeprice).ThenInclude(p => p!.Subject)
            .Include(b => b.Tutorsubjectgradeprice).ThenInclude(p => p!.Gradelevel)
            .Include(b => b.Package)
            .Include(b => b.ClassSessions)
            .Where(b => b.Parentid == parentId);

        if (!string.IsNullOrWhiteSpace(status))
            q = q.Where(b => b.Status == status);

        var total = await q.CountAsync();
        var items = await q.OrderByDescending(b => b.Createdat)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync();

        return (items, total);
    }

    public async Task<(IReadOnlyList<Booking> Items, int Total)> GetByStudentIdsPagedAsync(
        IEnumerable<string> studentIds, int page, int pageSize, string? status)
    {
        var ids = studentIds.ToList();
        var q = context.Bookings
            .AsNoTracking()
            .Include(b => b.Student)
            .Include(b => b.Student).ThenInclude(s => s!.GradelevelNavigation)
            .Include(b => b.Tutor).ThenInclude(t => t!.Tutor)
            .Include(b => b.Tutorsubjectgradeprice).ThenInclude(p => p!.Subject)
            .Include(b => b.Tutorsubjectgradeprice).ThenInclude(p => p!.Gradelevel)
            .Include(b => b.Package)
            .Include(b => b.ClassSessions)
            .Where(b => b.Studentid != null && ids.Contains(b.Studentid));

        if (!string.IsNullOrWhiteSpace(status))
            q = q.Where(b => b.Status == status);

        var total = await q.CountAsync();
        var items = await q.OrderByDescending(b => b.Createdat)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync();

        return (items, total);
    }

    public async Task<(IReadOnlyList<Booking> Items, int Total)> GetByTutorIdPagedAsync(
        string tutorId, int page, int pageSize, string? status)
    {
        var q = context.Bookings
            .AsNoTracking()
            .Include(b => b.Student)
            .Include(b => b.Student).ThenInclude(s => s!.GradelevelNavigation)
            .Include(b => b.Tutor).ThenInclude(t => t!.Tutor)
            .Include(b => b.Tutorsubjectgradeprice).ThenInclude(p => p!.Subject)
            .Include(b => b.Tutorsubjectgradeprice).ThenInclude(p => p!.Gradelevel)
            .Include(b => b.Package)
            .Include(b => b.ClassSessions)
            .Where(b => b.Tutorid == tutorId);

        if (!string.IsNullOrWhiteSpace(status))
        {
            // Hỗ trợ lọc nhiều status cùng lúc: "deposit_paid,ongoing,pending_remaining_payment"
            var statuses = status.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            q = statuses.Length == 1
                ? q.Where(b => b.Status == statuses[0])
                : q.Where(b => statuses.Contains(b.Status));
        }
        else
            q = q.Where(b => b.Status == BookingStatus.PendingTutor);

        var total = await q.CountAsync();
        var items = await q.OrderByDescending(b => b.Createdat)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync();

        return (items, total);
    }

    public Task<Booking?> FindForPaymentByUserAsync(int id, string userId, CancellationToken ct = default)
        => context.Bookings
            .Include(b => b.Student)
            .Include(b => b.Tutorsubjectgradeprice)
            .FirstOrDefaultAsync(b => b.Bookingid == id
                && (b.Parentid == userId || b.Studentid == userId || b.Student!.Linkeduserid == userId), ct);

    public Task<Booking?> FindTrackedAsync(int id, CancellationToken ct = default)
        => context.Bookings.FirstOrDefaultAsync(b => b.Bookingid == id, ct);

    public Task<bool> PaymentCodeExistsAsync(string paymentCode)
        => context.Bookings.AnyAsync(b => b.Paymentcode == paymentCode);

    public void Add(Booking booking)
        => context.Bookings.Add(booking);

    public Task<int> SaveChangesAsync()
        => context.SaveChangesAsync();
}
