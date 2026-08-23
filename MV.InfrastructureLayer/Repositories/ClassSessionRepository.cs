using Microsoft.EntityFrameworkCore;
using MV.DomainLayer.DTO;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Entities;
using MV.DomainLayer.Helpers;
using MV.InfrastructureLayer.DBContext;
using MV.ApplicationLayer.RepositoryInterfaces;
using MV.DomainLayer.Constants;
using System.Text.Json;
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
        string tutorId, int page, int pageSize, DateTime? fromDate, string? status, int? bookingId)
    {
        var q = context.ClassSessions
            .AsNoTracking()
            .Where(l => l.Tutorid == tutorId)
            .Include(l => l.Booking).ThenInclude(b => b!.Tutorsubjectgradeprice).ThenInclude(p => p!.Subject)
            .Include(l => l.Booking).ThenInclude(b => b!.Tutorsubjectgradeprice).ThenInclude(p => p!.Gradelevel)
            .Include(l => l.Booking).ThenInclude(b => b!.Student)
            .Include(l => l.Tutor).ThenInclude(t => t!.Tutor)
            .Include(l => l.RescheduleProposals)
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
        // Lọc theo lớp: màn chi tiết lớp cần đủ mọi buổi, kể cả buổi tháng khác.
        if (bookingId.HasValue)
            q = q.Where(l => l.Bookingid == bookingId.Value);

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
            .Include(l => l.ScheduleChanges)
            .Include(l => l.RescheduleProposals)
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

    public async Task<(IReadOnlyList<TutorClassAggregate> Items, int Total)> GetTutorClassesPagedAsync(
        string tutorId, int page, int pageSize, string? status, string? search, DateTime nowUtc)
    {
        // Source from Bookings (one row already = one "class") rather than GroupBy over sessions —
        // this translates cleanly to SQL (navigation props are 1-1) and lets us exclude dead/not-yet-
        // activated bookings up front. Session counts are correlated subqueries on b.ClassSessions.
        var grouped = context.Bookings
            .AsNoTracking()
            .Where(b => b.Tutorid == tutorId
                        && b.Status != BookingStatus.PendingTutor
                        && b.Status != BookingStatus.PendingPayment
                        && b.Status != BookingStatus.Accepted
                        && b.Status != BookingStatus.Cancelled
                        && b.Status != BookingStatus.CancelledNoshow
                        && b.Status != BookingStatus.PaymentTimeout)
            // Only count "activated" sessions: exclude both cancelled AND `reserved`. Sessions 2..N
            // are created up-front as `reserved` and stay invisible until the parent pays the remaining
            // amount — so a freshly-accepted booking must report 1 session, not the full package size.
            .Select(b => new TutorClassAggregate
            {
                BookingId = b.Bookingid,
                SubjectName = b.Tutorsubjectgradeprice!.Subject!.Subjectname,
                StudentName = b.Student!.Fullname,
                TotalSessions = b.ClassSessions.Count(l => l.Status != ClassSessionStatus.Cancelled && l.Status != ClassSessionStatus.CancelledNoshow && l.Status != ClassSessionStatus.Reserved),
                CompletedSessions = b.ClassSessions.Count(l => l.Status == ClassSessionStatus.Completed),
                ActiveSessions = b.ClassSessions.Count(l => l.Status != ClassSessionStatus.Cancelled && l.Status != ClassSessionStatus.CancelledNoshow && l.Status != ClassSessionStatus.Reserved),
                HasInProgress = b.ClassSessions.Any(l => l.Status == ClassSessionStatus.InProgress),
                HasPending = b.ClassSessions.Any(l => l.Status == ClassSessionStatus.PendingConfirmation),
                HasNonTerminal = b.ClassSessions.Any(l => l.Status != ClassSessionStatus.Completed
                                            && l.Status != ClassSessionStatus.Cancelled
                                            && l.Status != ClassSessionStatus.CancelledNoshow
                                            && l.Status != ClassSessionStatus.Reserved),
                NextSessionStart = b.ClassSessions
                    .Where(l => l.Scheduledstart > nowUtc
                                && l.Status != ClassSessionStatus.Cancelled
                                && l.Status != ClassSessionStatus.CancelledNoshow
                                && l.Status != ClassSessionStatus.Reserved)
                    .Min(l => (DateTime?)l.Scheduledstart),
                LatestStart = b.ClassSessions
                    .Where(l => l.Status != ClassSessionStatus.Reserved)
                    .Max(l => (DateTime?)l.Scheduledstart) ?? DateTime.MinValue,
            });

        // Only keep bookings that already have at least one activated session (not just reserved).
        grouped = grouped.Where(x => x.ActiveSessions > 0);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            grouped = grouped.Where(x =>
                (x.StudentName != null && x.StudentName.ToLower().Contains(term)) ||
                (x.SubjectName != null && x.SubjectName.ToLower().Contains(term)));
        }

        // Derive class status inline so we can filter by it at the DB.
        // completed → all non-cancelled sessions completed; else in_progress / pending / scheduled.
        if (!string.IsNullOrWhiteSpace(status))
        {
            grouped = status switch
            {
                ClassSessionStatus.Completed => grouped.Where(x => !x.HasNonTerminal),
                ClassSessionStatus.InProgress => grouped.Where(x => x.HasNonTerminal && x.HasInProgress),
                ClassSessionStatus.PendingConfirmation => grouped.Where(x => x.HasNonTerminal && !x.HasInProgress && x.HasPending),
                ClassSessionStatus.Scheduled => grouped.Where(x => x.HasNonTerminal && !x.HasInProgress && !x.HasPending),
                _ => grouped,
            };
        }

        var total = await grouped.CountAsync();

        // Sort by next upcoming session (classes with an upcoming session first).
        var items = await grouped
            .OrderBy(x => x.NextSessionStart == null ? 1 : 0)
            .ThenBy(x => x.NextSessionStart)
            .ThenByDescending(x => x.LatestStart)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        // Schedule string (distinct weekday+time slots, max 3) — cheap to build per page in memory.
        var bookingIds = items.Select(x => x.BookingId).ToList();
        var slots = await context.ClassSessions
            .AsNoTracking()
            .Where(l => l.Bookingid != null && bookingIds.Contains(l.Bookingid.Value)
                        && l.Status != ClassSessionStatus.Cancelled
                        && l.Status != ClassSessionStatus.CancelledNoshow
                        && l.Status != ClassSessionStatus.Reserved)
            .Select(l => new { BookingId = l.Bookingid!.Value, l.Scheduledstart })
            .ToListAsync();

        var scheduleByBooking = slots
            .GroupBy(s => s.BookingId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(s => s.Scheduledstart)
                      .Select(s => FormatSlot(s.Scheduledstart))
                      .Distinct()
                      .Take(3)
                      .ToList());

        foreach (var item in items)
            item.Schedule = scheduleByBooking.TryGetValue(item.BookingId, out var s) ? string.Join(", ", s) : null;

        return (items, total);
    }

    private static readonly string[] Weekdays = ["CN", "T2", "T3", "T4", "T5", "T6", "T7"];

    private static string FormatSlot(DateTime start)
        => $"{Weekdays[(int)start.DayOfWeek]} {start:HH:mm}";

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
            BookingStatus   = l.Booking?.Status,
            IsSettled       = l.Issettled,
            ScheduledStart  = l.Scheduledstart,
            ScheduledEnd    = l.Scheduledend,
            ConfirmDeadline = l.Confirmdeadline,
            ClassSessionPrice     = l.Lessonprice,
            SubjectName     = l.Booking?.Subject?.Subjectname,
            TutorName       = l.Booking?.Tutor?.Tutor?.Fullname,
            BookingId       = l.Bookingid,
            IsContinuation = l.Iscontinuation,
            IsDisputeRelearn = l.Isdisputerelearn,
            OriginalClassSessionId = l.Originalsessionid
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

        var scheduleChanges = await context.ClassSessionScheduleChanges
            .AsNoTracking()
            .Where(x => x.Classsessionid == classSessionId)
            .OrderBy(x => x.Schedulechangeid)
            .ToListAsync();
        var scheduleChangeConfirmerIds = scheduleChanges
            .SelectMany(x => new[] { x.Tutorconfirmedby, x.Learnerconfirmedby })
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Cast<string>()
            .Distinct()
            .ToList();
        var scheduleChangeConfirmerNames = await context.Users.AsNoTracking()
            .Where(x => scheduleChangeConfirmerIds.Contains(x.Userid))
            .ToDictionaryAsync(x => x.Userid, x => x.Fullname ?? x.Username ?? x.Email);

        return new StudentClassSessionDetailResponse
        {
            ClassSessionId        = classSession.Classsessionid,
            Status          = classSession.Status,
            BookingStatus   = classSession.Booking?.Status,
            IsSettled       = classSession.Issettled,
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
            IsContinuation = classSession.Iscontinuation,
            IsDisputeRelearn = classSession.Isdisputerelearn,
            SkipConfirmedByBothSides = classSession.Tutorskipconfirmedat.HasValue && classSession.Studentskipconfirmedat.HasValue,
            OriginalClassSessionId = classSession.Originalsessionid,
            Report          = classSession.ClassSessionReport == null ? null : new StudentClassSessionReportResponse
            {
                TopicsCovered    = classSession.ClassSessionReport.Contentcovered,
                HomeworkAssigned = classSession.ClassSessionReport.Homeworkassigned,
                TutorNotes       = classSession.Tutornotes,
                StudentPerformanceRating = classSession.ClassSessionReport.Studentperformancerating,
                // Portal học sinh chỉ cần URL, nhưng vẫn phải đọc qua serializer chung vì cột này
                // giờ lưu mảng object {url, description}.
                Attachments      = ReportAttachmentSerializer.ToUrls(
                    ReportAttachmentSerializer.Deserialize(classSession.ClassSessionReport.Attachments))
            },
            ScheduleChanges = scheduleChanges.Select(x => new DisputeScheduleChangeAuditResponse
            {
                ScheduleChangeId = x.Schedulechangeid,
                Status = x.Status,
                OriginalScheduledStart = x.Originalscheduledstart,
                OriginalScheduledEnd = x.Originalscheduledend,
                AdjustedScheduledStart = x.Adjustedscheduledstart,
                AdjustedScheduledEnd = x.Adjustedscheduledend,
                LearnerApproverRole = x.Learnerapproverrole,
                TutorConfirmedByName = x.Tutorconfirmedby != null && scheduleChangeConfirmerNames.TryGetValue(x.Tutorconfirmedby, out var tutorName) ? tutorName : null,
                TutorConfirmedAt = x.Tutorconfirmedat,
                LearnerConfirmedByName = x.Learnerconfirmedby != null && scheduleChangeConfirmerNames.TryGetValue(x.Learnerconfirmedby, out var learnerName) ? learnerName : null,
                LearnerConfirmedAt = x.Learnerconfirmedat,
                RequestedAt = x.Requestedat,
                ApprovedAt = x.Approvedat,
                AppliedAt = x.Appliedat
            }).ToList()
        };
    }

    /// <summary>Deserialize JSON array or legacy comma-separated string.</summary>
    private static List<string>? DeserializeJsonList(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        if (value.TrimStart().StartsWith('['))
        {
            try { return JsonSerializer.Deserialize<List<string>>(value); }
            catch { /* fall through to comma-split */ }
        }

        return value.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
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
            BookingStatus   = l.Booking?.Status,
            IsSettled       = l.Issettled,
            ScheduledStart  = l.Scheduledstart,
            ScheduledEnd    = l.Scheduledend,
            ConfirmDeadline = l.Confirmdeadline,
            SubjectName     = l.Booking?.Subject?.Subjectname,
            TutorName       = l.Booking?.Tutor?.Tutor?.Fullname,
            BookingId       = l.Bookingid,
            IsContinuation = l.Iscontinuation,
            IsDisputeRelearn = l.Isdisputerelearn,
            OriginalClassSessionId = l.Originalsessionid
        }).ToList();
    }

    public Task<int> SaveChangesAsync(CancellationToken ct = default)
        => context.SaveChangesAsync(ct);
}
