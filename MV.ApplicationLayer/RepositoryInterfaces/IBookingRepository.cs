using MV.DomainLayer.Entities;

namespace MV.ApplicationLayer.RepositoryInterfaces;

public interface IBookingRepository
{
    // Single-entity lookups
    Task<Booking?> FindByIdAsync(int id);
    Task<Booking?> FindWithStudentAsync(int id);
    Task<Booking?> FindWithRelationsAsync(int id);   // includes Student, Tutor.Tutor, Subject, ClassSessions
    /// <summary>
    /// Loads and row-locks a booking, then loads the same relations as FindWithRelationsAsync.
    /// The caller must already have an active database transaction.
    /// </summary>
    Task<Booking?> FindWithRelationsForUpdateAsync(int id, CancellationToken ct = default);
    Task<Booking?> FindByIdForUserAsync(int id, string userId);  // owned by parent/student/tutor
    /// <summary>Loads booking with Student include for payment ownership checks. Tracked. Supports CT.</summary>
    Task<Booking?> FindForPaymentByUserAsync(int id, string userId, CancellationToken ct = default);
    /// <summary>Loads booking without navigations, tracked for mutation. Supports CT.</summary>
    Task<Booking?> FindTrackedAsync(int id, CancellationToken ct = default);

    // Paged listing
    Task<(IReadOnlyList<Booking> Items, int Total)> GetByParentIdPagedAsync(string parentId, int page, int pageSize, string? status);
    Task<(IReadOnlyList<Booking> Items, int Total)> GetByStudentIdsPagedAsync(IEnumerable<string> studentIds, int page, int pageSize, string? status);
    Task<(IReadOnlyList<Booking> Items, int Total)> GetByTutorIdPagedAsync(string tutorId, int page, int pageSize, string? status);

    // Existence checks
    Task<bool> PaymentCodeExistsAsync(string paymentCode);

    // Mutations
    void Add(Booking booking);

    Task<int> SaveChangesAsync();
}
