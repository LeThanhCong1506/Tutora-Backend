using Microsoft.EntityFrameworkCore;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Entities;
using MV.DomainLayer.Helpers;
using MV.InfrastructureLayer.DBContext;
using MV.ApplicationLayer.RepositoryInterfaces;
using static MV.DomainLayer.Constants.ClassSessionStatus;
namespace MV.InfrastructureLayer.Repositories;

public class ClassSessionRepository(AgoraDbContext context) : IClassSessionRepository
{
    public Task<int> CountForBookingAsync(int bookingId, CancellationToken ct = default)
        => context.ClassSessions.CountAsync(l => l.Bookingid == bookingId, ct);

    public Task<bool> HasConflictAsync(string tutorId, DateTime start, DateTime end, CancellationToken ct = default)
        => context.ClassSessions.AsNoTracking()
            .AnyAsync(l => l.Tutorid == tutorId
                        && l.Status != Cancelled
                        && l.Scheduledstart < end
                        && l.Scheduledend > start, ct);

    public void Add(ClassSession classSession)
        => context.ClassSessions.Add(classSession);

    public async Task<(IReadOnlyList<ClassSession> Items, int Total)> GetTutorClassSessionsPagedAsync(
        string tutorId, int page, int pageSize, DateTime? fromDate, string? status)
    {
        var q = context.ClassSessions
            .AsNoTracking()
            .Where(l => l.Tutorid == tutorId)
            .Include(l => l.Booking).ThenInclude(b => b!.Tutorsubjectgradeprice).ThenInclude(p => p!.Subject)
            .Include(l => l.Booking).ThenInclude(b => b!.Tutorsubjectgradeprice).ThenInclude(p => p!.Gradelevel)
            .Include(l => l.Booking).ThenInclude(b => b!.Student)
            .Include(l => l.Tutor).ThenInclude(t => t!.Tutor)
            .AsQueryable();

        if (fromDate.HasValue)
        {
            // Normalize timezone: nếu UTC thì giữ nguyên, nếu Unspecified thì coi như user time
            var fromUtc = fromDate.Value.Kind == DateTimeKind.Utc 
                ? fromDate.Value 
                : DateTime.SpecifyKind(fromDate.Value, DateTimeKind.Utc);
            q = q.Where(l => l.Scheduledstart >= fromUtc);
        }
        if (!string.IsNullOrWhiteSpace(status))
            q = q.Where(l => l.Status == status);

        q = q.OrderBy(l => l.Scheduledstart);

        var total = await q.CountAsync();
        var items = await q.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return (items, total);
    }

    public async Task<(IReadOnlyList<ClassSession> Items, int Total)> GetByStudentIdsPagedAsync(
        IEnumerable<string> studentIds, int page, int pageSize, DateTime? fromDate, string? status)
    {
        var ids = studentIds.ToList();
        var q = context.ClassSessions
            .AsNoTracking()
            .Where(l => l.Studentid != null && ids.Contains(l.Studentid))
            .Include(l => l.Booking).ThenInclude(b => b!.Tutorsubjectgradeprice).ThenInclude(p => p!.Subject)
            .Include(l => l.Booking).ThenInclude(b => b!.Tutorsubjectgradeprice).ThenInclude(p => p!.Gradelevel)
            .Include(l => l.Booking).ThenInclude(b => b!.Student)
            .Include(l => l.Tutor).ThenInclude(t => t!.Tutor)
            .AsQueryable();

        if (fromDate.HasValue)
        {
            // Normalize timezone: nếu UTC thì giữ nguyên, nếu Unspecified thì coi như user time
            var fromUtc = fromDate.Value.Kind == DateTimeKind.Utc 
                ? fromDate.Value 
                : DateTime.SpecifyKind(fromDate.Value, DateTimeKind.Utc);
            q = q.Where(l => l.Scheduledstart >= fromUtc);
        }
        if (!string.IsNullOrWhiteSpace(status))
            q = q.Where(l => l.Status == status);

        q = q.OrderBy(l => l.Scheduledstart);

        var total = await q.CountAsync();
        var items = await q.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return (items, total);
    }

    public Task<ClassSession?> GetByIdWithDetailsAsync(int classSessionId)
        => context.ClassSessions
            .AsNoTracking()
            .Include(l => l.Booking).ThenInclude(b => b!.Tutorsubjectgradeprice).ThenInclude(p => p!.Subject)
            .Include(l => l.Booking).ThenInclude(b => b!.Tutorsubjectgradeprice).ThenInclude(p => p!.Gradelevel)
            .Include(l => l.Booking).ThenInclude(b => b!.Student)
            .Include(l => l.Tutor).ThenInclude(t => t!.Tutor)
            .FirstOrDefaultAsync(l => l.Classsessionid == classSessionId);

    public async Task<(IReadOnlyList<StudentClassSessionSummaryResponse> Items, int Total)> GetStudentClassSessionsPagedAsync(
        string studentId, int page, int pageSize, string? status)
    {
        var q = context.ClassSessions
            .AsNoTracking()
            .Include(l => l.Booking).ThenInclude(b => b!.Tutorsubjectgradeprice).ThenInclude(p => p!.Subject)
            .Include(l => l.Booking).ThenInclude(b => b!.Tutor).ThenInclude(t => t!.Tutor)
            .Where(l => l.Studentid == studentId);

        if (!string.IsNullOrEmpty(status))
            q = q.Where(l => l.Status == status);

        var total = await q.CountAsync();

        var rawItems = await q
            .OrderByDescending(l => l.Scheduledstart)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync();

        var items = rawItems.Select(l => new StudentClassSessionSummaryResponse
        {
            ClassSessionId        = l.Classsessionid,
            Status          = l.Status,
            ScheduledStart  = l.Scheduledstart,
            ScheduledEnd    = l.Scheduledend,
            ConfirmDeadline = l.Confirmdeadline,
            ClassSessionPrice     = l.Lessonprice,
            SubjectName     = l.Booking?.Subject?.Subjectname,
            TutorName       = l.Booking?.Tutor?.Tutor?.Fullname,
            BookingId       = l.Bookingid
        }).ToList();

        return (items, total);
    }

    public async Task<StudentClassSessionDetailResponse?> GetStudentClassSessionDetailAsync(int classSessionId, string studentId)
    {
        var classSession = await context.ClassSessions
            .AsNoTracking()
            .Include(l => l.Booking).ThenInclude(b => b!.Tutorsubjectgradeprice).ThenInclude(p => p!.Subject)
            .Include(l => l.Booking).ThenInclude(b => b!.Tutor).ThenInclude(t => t!.Tutor)
            .Include(l => l.ClassSessionReport)
            .Where(l => l.Classsessionid == classSessionId && l.Studentid == studentId)
            .FirstOrDefaultAsync();

        if (classSession == null) return null;

        return new StudentClassSessionDetailResponse
        {
            ClassSessionId        = classSession.Classsessionid,
            Status          = classSession.Status,
            ScheduledStart  = classSession.Scheduledstart,
            ScheduledEnd    = classSession.Scheduledend,
            ConfirmDeadline = classSession.Confirmdeadline,
            ClassSessionPrice     = classSession.Lessonprice,
            MeetingLink     = classSession.Meetinglink,
            CheckinTime     = classSession.Checkintime,
            CheckoutTime    = classSession.Checkouttime,
            SubjectName     = classSession.Booking?.Subject?.Subjectname,
            TutorName       = classSession.Booking?.Tutor?.Tutor?.Fullname,
            TutorAvatar     = classSession.Booking?.Tutor?.Tutor?.Avatarurl,
            BookingId       = classSession.Bookingid,
            Report          = classSession.ClassSessionReport == null ? null : new StudentClassSessionReportResponse
            {
                TopicsCovered    = classSession.ClassSessionReport.Contentcovered,
                HomeworkAssigned = classSession.ClassSessionReport.Homeworkassigned,
                TutorNotes       = classSession.ClassSessionReport.Studentperformancerating.ToString()
            }
        };
    }

    public async Task<IReadOnlyList<StudentClassSessionSummaryResponse>> GetStudentPendingClassSessionsAsync(string studentId)
    {
        var rawItems = await context.ClassSessions
            .AsNoTracking()
            .Include(l => l.Booking).ThenInclude(b => b!.Tutorsubjectgradeprice).ThenInclude(p => p!.Subject)
            .Include(l => l.Booking).ThenInclude(b => b!.Tutor).ThenInclude(t => t!.Tutor)
            .Where(l => l.Studentid == studentId && l.Status == PendingConfirmation)
            .OrderBy(l => l.Confirmdeadline)
            .ToListAsync();

        return rawItems.Select(l => new StudentClassSessionSummaryResponse
        {
            ClassSessionId        = l.Classsessionid,
            Status          = l.Status,
            ScheduledStart  = l.Scheduledstart,
            ScheduledEnd    = l.Scheduledend,
            ConfirmDeadline = l.Confirmdeadline,
            SubjectName     = l.Booking?.Subject?.Subjectname,
            TutorName       = l.Booking?.Tutor?.Tutor?.Fullname,
            BookingId       = l.Bookingid
        }).ToList();
    }

    public Task<int> SaveChangesAsync(CancellationToken ct = default)
        => context.SaveChangesAsync(ct);
}
