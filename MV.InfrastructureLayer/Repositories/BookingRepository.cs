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
            .Include(b => b.Paymentrequests)
            .FirstOrDefaultAsync(b => b.Bookingid == id);

    public async Task<Booking?> FindWithRelationsForUpdateAsync(int id, CancellationToken ct = default)
    {
        var booking = await context.Bookings
            .FromSqlRaw(SqlQueries.LockBookingById, id)
            .SingleOrDefaultAsync(ct);

        if (booking == null) return null;

        // FromSqlRaw still resolves to an already-tracked instance when the
        // same DbContext read this booking earlier. The SELECT above acquires
        // the row lock, but ReloadAsync is required to replace stale scalar
        // values with the state that won the lock.
        await context.Entry(booking).ReloadAsync(ct);

        await context.Entry(booking).Reference(b => b.Student).LoadAsync(ct);
        if (booking.Student != null)
        {
            await context.Entry(booking.Student).Reference(s => s.Linkeduser).LoadAsync(ct);
            await context.Entry(booking.Student).Reference(s => s.GradelevelNavigation).LoadAsync(ct);
        }

        await context.Entry(booking).Reference(b => b.Tutor).LoadAsync(ct);
        if (booking.Tutor != null)
            await context.Entry(booking.Tutor).Reference(t => t.Tutor).LoadAsync(ct);

        await context.Entry(booking).Reference(b => b.Tutorsubjectgradeprice).LoadAsync(ct);
        if (booking.Tutorsubjectgradeprice != null)
        {
            await context.Entry(booking.Tutorsubjectgradeprice).Reference(p => p.Subject).LoadAsync(ct);
            await context.Entry(booking.Tutorsubjectgradeprice).Reference(p => p.Gradelevel).LoadAsync(ct);
        }

        await context.Entry(booking).Reference(b => b.Package).LoadAsync(ct);
        await context.Entry(booking).Collection(b => b.ClassSessions).LoadAsync(ct);
        await context.Entry(booking).Collection(b => b.Paymentrequests).LoadAsync(ct);
        return booking;
    }

    public Task<Booking?> FindByIdForUserAsync(int id, string userId)
        => context.Bookings
            .Include(b => b.Student)
            .Include(b => b.Student).ThenInclude(s => s!.GradelevelNavigation)
            .Include(b => b.Tutor).ThenInclude(t => t!.Tutor)
            .Include(b => b.Tutorsubjectgradeprice).ThenInclude(p => p!.Subject)
            .Include(b => b.Tutorsubjectgradeprice).ThenInclude(p => p!.Gradelevel)
            .Include(b => b.Package)
            .Include(b => b.ClassSessions)
            .Include(b => b.Paymentrequests)
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
            .Include(b => b.Paymentrequests)
            .Where(b => b.Parentid == parentId);

        if (!string.IsNullOrWhiteSpace(status))
        {
            // Hỗ trợ lọc nhiều status cùng lúc: "cancelled,cancelled_noshow,payment_timeout"
            var statuses = status.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            q = statuses.Length == 1
                ? q.Where(b => b.Status == statuses[0])
                : q.Where(b => statuses.Contains(b.Status));
        }

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
            .Include(b => b.Paymentrequests)
            .Where(b => b.Studentid != null && ids.Contains(b.Studentid));

        if (!string.IsNullOrWhiteSpace(status))
        {
            // Hỗ trợ lọc nhiều status cùng lúc: "cancelled,cancelled_noshow,payment_timeout"
            var statuses = status.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            q = statuses.Length == 1
                ? q.Where(b => b.Status == statuses[0])
                : q.Where(b => statuses.Contains(b.Status));
        }

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
            .Include(b => b.Paymentrequests)
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
            .AsNoTracking()
            .Include(b => b.Student)
            .Include(b => b.Tutorsubjectgradeprice)
            .FirstOrDefaultAsync(b => b.Bookingid == id
                && (b.Parentid == userId || b.Studentid == userId || b.Student!.Linkeduserid == userId), ct);

    public Task<Booking?> FindTrackedAsync(int id, CancellationToken ct = default)
        => context.Bookings.FirstOrDefaultAsync(b => b.Bookingid == id, ct);

    public Task<bool> PaymentCodeExistsAsync(string paymentCode)
        => context.PaymentRequests.AnyAsync(r => r.Paymentlinkid == paymentCode);

    public void Add(Booking booking)
        => context.Bookings.Add(booking);

    public Task<int> SaveChangesAsync()
        => context.SaveChangesAsync();
}
