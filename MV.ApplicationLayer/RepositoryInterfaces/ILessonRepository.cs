using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Entities;

namespace MV.ApplicationLayer.RepositoryInterfaces;

public interface ILessonRepository
{
    /// <summary>Returns the number of lessons already created for a booking.</summary>
    Task<int> CountForBookingAsync(int bookingId, CancellationToken ct = default);

    /// <summary>Checks whether any non-cancelled lesson for the given tutor overlaps [start, end).</summary>
    Task<bool> HasConflictAsync(string tutorId, DateTime start, DateTime end, CancellationToken ct = default);

    void Add(Lesson lesson);

    Task<(IReadOnlyList<Lesson> Items, int Total)> GetTutorLessonsPagedAsync(
        string tutorId, int page, int pageSize, DateTime? fromDate, string? status);

    Task<(IReadOnlyList<Lesson> Items, int Total)> GetByStudentIdsPagedAsync(
        IEnumerable<string> studentIds, int page, int pageSize, DateTime? fromDate, string? status);

    /// <summary>Loads a lesson with all navigation props needed for LessonResponse mapping.</summary>
    Task<Lesson?> GetByIdWithDetailsAsync(int lessonId);

    // --- Student-facing projection methods (projection done at DB level) ---

    Task<(IReadOnlyList<StudentLessonSummaryResponse> Items, int Total)> GetStudentLessonsPagedAsync(
        string studentId, int page, int pageSize, string? status);

    Task<StudentLessonDetailResponse?> GetStudentLessonDetailAsync(int lessonId, string studentId);

    Task<IReadOnlyList<StudentLessonSummaryResponse>> GetStudentPendingLessonsAsync(string studentId);

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
