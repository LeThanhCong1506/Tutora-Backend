using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Entities;

namespace MV.ApplicationLayer.RepositoryInterfaces;

public interface IClassSessionRepository
{
    /// <summary>Returns the number of classSessions already created for a booking.</summary>
    Task<int> CountForBookingAsync(int bookingId, CancellationToken ct = default);

    /// <summary>Checks whether any non-cancelled classSession for the given tutor overlaps [start, end).</summary>
    Task<bool> HasConflictAsync(string tutorId, DateTime start, DateTime end, CancellationToken ct = default);

    void Add(ClassSession classSession);

    Task<(IReadOnlyList<ClassSession> Items, int Total)> GetTutorClassSessionsPagedAsync(
        string tutorId, int page, int pageSize, DateTime? fromDate, string? status, int? bookingId);

    Task<(IReadOnlyList<ClassSession> Items, int Total)> GetByStudentIdsPagedAsync(
        IEnumerable<string> studentIds, int page, int pageSize, DateTime? fromDate, string? status);

    Task<(IReadOnlyList<TutorClassAggregate> Items, int Total)> GetTutorClassesPagedAsync(
        string tutorId, int page, int pageSize, string? status, string? search, DateTime nowUtc);

    /// <summary>Loads a classSession with all navigation props needed for ClassSessionResponse mapping.</summary>
    Task<ClassSession?> GetByIdWithDetailsAsync(int classSessionId);

    // --- Student-facing projection methods (projection done at DB level) ---

    Task<(IReadOnlyList<StudentClassSessionSummaryResponse> Items, int Total)> GetStudentClassSessionsPagedAsync(
        string studentId, int page, int pageSize, string? status);

    Task<StudentClassSessionDetailResponse?> GetStudentClassSessionDetailAsync(int classSessionId, string studentId);

    Task<IReadOnlyList<StudentClassSessionSummaryResponse>> GetStudentPendingClassSessionsAsync(string studentId);

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
