using Microsoft.EntityFrameworkCore;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Entities;
using MV.InfrastructureLayer.DBContext;
using MV.ApplicationLayer.RepositoryInterfaces;
using static MV.DomainLayer.Constants.LessonStatus;
using static MV.DomainLayer.Helpers.VietnamTimeHelper;

namespace MV.InfrastructureLayer.Repositories;

public class LessonRepository(AgoraDbContext context) : ILessonRepository
{
    public Task<int> CountForBookingAsync(int bookingId, CancellationToken ct = default)
        => context.Lessons.CountAsync(l => l.Bookingid == bookingId, ct);

    public Task<bool> HasConflictAsync(string tutorId, DateTime start, DateTime end, CancellationToken ct = default)
        => context.Lessons.AsNoTracking()
            .AnyAsync(l => l.Tutorid == tutorId
                        && l.Status != Cancelled
                        && l.Scheduledstart < end
                        && l.Scheduledend > start, ct);

    public void Add(Lesson lesson)
        => context.Lessons.Add(lesson);

    public async Task<(IReadOnlyList<Lesson> Items, int Total)> GetTutorLessonsPagedAsync(
        string tutorId, int page, int pageSize, DateTime? fromDate, string? status)
    {
        var q = context.Lessons
            .AsNoTracking()
            .Where(l => l.Tutorid == tutorId)
            .Include(l => l.Booking).ThenInclude(b => b!.Tutorsubjectgradeprice).ThenInclude(p => p!.Subject)
            .Include(l => l.Booking).ThenInclude(b => b!.Tutorsubjectgradeprice).ThenInclude(p => p!.Gradelevel)
            .Include(l => l.Booking).ThenInclude(b => b!.Student)
            .Include(l => l.Tutor).ThenInclude(t => t!.Tutor)
            .AsQueryable();

        if (fromDate.HasValue)
        {
            // Normalize timezone: nếu UTC thì giữ nguyên, nếu Unspecified thì coi như VN time
            var fromUtc = fromDate.Value.Kind == DateTimeKind.Utc 
                ? fromDate.Value 
                : MV.DomainLayer.Helpers.VietnamTimeHelper.ToUtc(fromDate.Value);
            q = q.Where(l => l.Scheduledstart >= fromUtc);
        }
        if (!string.IsNullOrWhiteSpace(status))
            q = q.Where(l => l.Status == status);

        q = q.OrderBy(l => l.Scheduledstart);

        var total = await q.CountAsync();
        var items = await q.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return (items, total);
    }

    public async Task<(IReadOnlyList<Lesson> Items, int Total)> GetByStudentIdsPagedAsync(
        IEnumerable<string> studentIds, int page, int pageSize, DateTime? fromDate, string? status)
    {
        var ids = studentIds.ToList();
        var q = context.Lessons
            .AsNoTracking()
            .Where(l => l.Studentid != null && ids.Contains(l.Studentid))
            .Include(l => l.Booking).ThenInclude(b => b!.Tutorsubjectgradeprice).ThenInclude(p => p!.Subject)
            .Include(l => l.Booking).ThenInclude(b => b!.Tutorsubjectgradeprice).ThenInclude(p => p!.Gradelevel)
            .Include(l => l.Booking).ThenInclude(b => b!.Student)
            .Include(l => l.Tutor).ThenInclude(t => t!.Tutor)
            .AsQueryable();

        if (fromDate.HasValue)
        {
            // Normalize timezone: nếu UTC thì giữ nguyên, nếu Unspecified thì coi như VN time
            var fromUtc = fromDate.Value.Kind == DateTimeKind.Utc 
                ? fromDate.Value 
                : MV.DomainLayer.Helpers.VietnamTimeHelper.ToUtc(fromDate.Value);
            q = q.Where(l => l.Scheduledstart >= fromUtc);
        }
        if (!string.IsNullOrWhiteSpace(status))
            q = q.Where(l => l.Status == status);

        q = q.OrderBy(l => l.Scheduledstart);

        var total = await q.CountAsync();
        var items = await q.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return (items, total);
    }

    public Task<Lesson?> GetByIdWithDetailsAsync(int lessonId)
        => context.Lessons
            .AsNoTracking()
            .Include(l => l.Booking).ThenInclude(b => b!.Tutorsubjectgradeprice).ThenInclude(p => p!.Subject)
            .Include(l => l.Booking).ThenInclude(b => b!.Tutorsubjectgradeprice).ThenInclude(p => p!.Gradelevel)
            .Include(l => l.Booking).ThenInclude(b => b!.Student)
            .Include(l => l.Tutor).ThenInclude(t => t!.Tutor)
            .FirstOrDefaultAsync(l => l.Lessonid == lessonId);

    public async Task<(IReadOnlyList<StudentLessonSummaryResponse> Items, int Total)> GetStudentLessonsPagedAsync(
        string studentId, int page, int pageSize, string? status)
    {
        var q = context.Lessons
            .AsNoTracking()
            .Include(l => l.Booking).ThenInclude(b => b!.Tutorsubjectgradeprice).ThenInclude(p => p!.Subject)
            .Include(l => l.Booking).ThenInclude(b => b!.Tutor).ThenInclude(t => t!.Tutor)
            .Where(l => l.Studentid == studentId);

        if (!string.IsNullOrEmpty(status))
            q = q.Where(l => l.Status == status);

        var total = await q.CountAsync();

        // Load entities into memory first — ToVietnamTime() cannot be translated to SQL
        var rawItems = await q
            .OrderByDescending(l => l.Scheduledstart)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync();

        var items = rawItems.Select(l => new StudentLessonSummaryResponse
        {
            LessonId        = l.Lessonid,
            Status          = l.Status,
            ScheduledStart  = ToVietnamTime(l.Scheduledstart),
            ScheduledEnd    = ToVietnamTime(l.Scheduledend),
            ConfirmDeadline = l.Confirmdeadline.HasValue ? ToVietnamTime(l.Confirmdeadline.Value) : null,
            LessonPrice     = l.Lessonprice,
            SubjectName     = l.Booking?.Subject?.Subjectname,
            TutorName       = l.Booking?.Tutor?.Tutor?.Fullname,
            BookingId       = l.Bookingid
        }).ToList();

        return (items, total);
    }

    public async Task<StudentLessonDetailResponse?> GetStudentLessonDetailAsync(int lessonId, string studentId)
    {
        // Load entity into memory first — ToVietnamTime() cannot be translated to SQL
        var lesson = await context.Lessons
            .AsNoTracking()
            .Include(l => l.Booking).ThenInclude(b => b!.Tutorsubjectgradeprice).ThenInclude(p => p!.Subject)
            .Include(l => l.Booking).ThenInclude(b => b!.Tutor).ThenInclude(t => t!.Tutor)
            .Include(l => l.Lessonreport)
            .Where(l => l.Lessonid == lessonId && l.Studentid == studentId)
            .FirstOrDefaultAsync();

        if (lesson == null) return null;

        return new StudentLessonDetailResponse
        {
            LessonId        = lesson.Lessonid,
            Status          = lesson.Status,
            ScheduledStart  = ToVietnamTime(lesson.Scheduledstart),
            ScheduledEnd    = ToVietnamTime(lesson.Scheduledend),
            ConfirmDeadline = lesson.Confirmdeadline.HasValue ? ToVietnamTime(lesson.Confirmdeadline.Value) : null,
            LessonPrice     = lesson.Lessonprice,
            MeetingLink     = lesson.Meetinglink,
            CheckinTime     = lesson.Checkintime.HasValue  ? ToVietnamTime(lesson.Checkintime.Value)  : null,
            CheckoutTime    = lesson.Checkouttime.HasValue ? ToVietnamTime(lesson.Checkouttime.Value) : null,
            SubjectName     = lesson.Booking?.Subject?.Subjectname,
            TutorName       = lesson.Booking?.Tutor?.Tutor?.Fullname,
            TutorAvatar     = lesson.Booking?.Tutor?.Tutor?.Avatarurl,
            BookingId       = lesson.Bookingid,
            Report          = lesson.Lessonreport == null ? null : new StudentLessonReportResponse
            {
                TopicsCovered    = lesson.Lessonreport.Contentcovered,
                HomeworkAssigned = lesson.Lessonreport.Homeworkassigned,
                TutorNotes       = lesson.Lessonreport.Studentperformancerating.ToString()
            }
        };
    }

    public async Task<IReadOnlyList<StudentLessonSummaryResponse>> GetStudentPendingLessonsAsync(string studentId)
    {
        // Load entities into memory first — ToVietnamTime() cannot be translated to SQL
        var rawItems = await context.Lessons
            .AsNoTracking()
            .Include(l => l.Booking).ThenInclude(b => b!.Tutorsubjectgradeprice).ThenInclude(p => p!.Subject)
            .Include(l => l.Booking).ThenInclude(b => b!.Tutor).ThenInclude(t => t!.Tutor)
            .Where(l => l.Studentid == studentId && l.Status == PendingConfirmation)
            .OrderBy(l => l.Confirmdeadline)
            .ToListAsync();

        return rawItems.Select(l => new StudentLessonSummaryResponse
        {
            LessonId        = l.Lessonid,
            Status          = l.Status,
            ScheduledStart  = ToVietnamTime(l.Scheduledstart),
            ScheduledEnd    = ToVietnamTime(l.Scheduledend),
            ConfirmDeadline = l.Confirmdeadline.HasValue ? ToVietnamTime(l.Confirmdeadline.Value) : null,
            SubjectName     = l.Booking?.Subject?.Subjectname,
            TutorName       = l.Booking?.Tutor?.Tutor?.Fullname,
            BookingId       = l.Bookingid
        }).ToList();
    }

    public Task<int> SaveChangesAsync(CancellationToken ct = default)
        => context.SaveChangesAsync(ct);
}
