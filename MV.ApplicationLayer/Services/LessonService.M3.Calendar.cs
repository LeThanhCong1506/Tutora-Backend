using Microsoft.EntityFrameworkCore;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Helpers;
using static MV.DomainLayer.Constants.LessonStatus;
namespace MV.ApplicationLayer.Services;

public partial class LessonService
{
    // ── M3-T1: Calendar & Dashboard ───────────────────────────────────────────

    public async Task<List<CalendarDayResponse>> GetTutorCalendarAsync(string tutorId, DateTime startDate, DateTime endDate)
    {
        // Normalize timezone: nếu frontend gửi UTC thì giữ nguyên, nếu Unspecified thì coi như user time và convert sang UTC
        var startUtc = startDate.Kind == DateTimeKind.Utc 
            ? startDate 
            : DateTime.SpecifyKind(startDate, DateTimeKind.Utc);
        var endUtc = endDate.Kind == DateTimeKind.Utc 
            ? endDate 
            : DateTime.SpecifyKind(endDate, DateTimeKind.Utc);

        var lessons = await _context.Lessons
            .AsNoTracking()
            .Where(l => l.Tutorid == tutorId && l.Scheduledstart >= startUtc && l.Scheduledstart <= endUtc)
            .Include(l => l.Booking)
                .ThenInclude(b => b!.Tutorsubjectgradeprice)
                    .ThenInclude(p => p!.Subject)
            .Include(l => l.Booking)
                .ThenInclude(b => b!.Student)
            .OrderBy(l => l.Scheduledstart)
            .ToListAsync();

        // Group theo NGÀY Việt Nam để tránh lệch ngày do UTC+7
        var grouped = lessons
            .GroupBy(l => l.Scheduledstart.Date)
            .Select(g => new CalendarDayResponse
            {
                Date = g.Key,
                Lessons = g.Select(l => new CalendarLessonResponse
                {
                    LessonId = l.Lessonid,
                    ScheduledStart = l.Scheduledstart,
                    ScheduledEnd = l.Scheduledend,
                    StudentName = l.Booking?.Student?.Fullname,
                    SubjectName = l.Booking?.Subject?.Subjectname,
                    Status = l.Status,
                    MeetingLink = l.Meetinglink
                }).ToList()
            })
            .ToList();

        return grouped;
    }

    public async Task<List<CalendarDayResponse>> GetStudentCalendarAsync(string studentUserId, DateTime startDate, DateTime endDate)
    {
        // Normalize timezone
        var startUtc = startDate.Kind == DateTimeKind.Utc 
            ? startDate 
            : DateTime.SpecifyKind(startDate, DateTimeKind.Utc);
        var endUtc = endDate.Kind == DateTimeKind.Utc 
            ? endDate 
            : DateTime.SpecifyKind(endDate, DateTimeKind.Utc);

        // Resolve studentId từ studentId hoặc linkedUserId (account tự đăng ký)
        var profile = await _context.Studentprofiles
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Studentid == studentUserId || s.Linkeduserid == studentUserId);

        if (profile == null)
            return new List<CalendarDayResponse>();

        var lessons = await _context.Lessons
            .AsNoTracking()
            .Where(l => l.Studentid == profile.Studentid
                     && l.Scheduledstart >= startUtc
                     && l.Scheduledstart <= endUtc)
            .Include(l => l.Booking)
                .ThenInclude(b => b!.Tutorsubjectgradeprice)
                    .ThenInclude(p => p!.Subject)
            .Include(l => l.Booking)
                .ThenInclude(b => b!.Tutor)
                    .ThenInclude(t => t!.Tutor)
            .OrderBy(l => l.Scheduledstart)
            .ToListAsync();

        return lessons
            .GroupBy(l => l.Scheduledstart.Date)
            .Select(g => new CalendarDayResponse
            {
                Date = g.Key,
                Lessons = g.Select(l => new CalendarLessonResponse
                {
                    LessonId = l.Lessonid,
                    ScheduledStart = l.Scheduledstart,
                    ScheduledEnd = l.Scheduledend,
                    TutorName = l.Booking?.Tutor?.Tutor?.Fullname,
                    SubjectName = l.Booking?.Subject?.Subjectname,
                    Status = l.Status,
                    MeetingLink = l.Meetinglink
                }).ToList()
            })
            .ToList();
    }

    public async Task<TutorDashboardStatsResponse> GetTutorDashboardStatsAsync(string tutorId)
    {
        var now = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
        var startOfMonth = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        var upcomingCount = await _context.Lessons
            .CountAsync(l => l.Tutorid == tutorId && l.Status == Scheduled && l.Scheduledstart > now);

        var completedThisMonth = await _context.Lessons
            .CountAsync(l => l.Tutorid == tutorId && l.Status == Completed && l.Scheduledstart >= startOfMonth);

        var totalCompleted = await _context.Lessons
            .CountAsync(l => l.Tutorid == tutorId && l.Status == Completed);

        var earningsThisMonth = await _context.Lessons
            .Where(l => l.Tutorid == tutorId && l.Status == Completed && l.Issettled == true && l.Scheduledstart >= startOfMonth)
            .SumAsync(l => l.Lessonprice ?? 0);

        var totalEarnings = await _context.Lessons
            .Where(l => l.Tutorid == tutorId && l.Status == Completed && l.Issettled == true)
            .SumAsync(l => l.Lessonprice ?? 0);

        var wallet = await _context.Wallets.FirstOrDefaultAsync(w => w.Userid == tutorId);

        var pendingConfirmation = await _context.Lessons
            .CountAsync(l => l.Tutorid == tutorId && l.Status == PendingConfirmation);

        var activeDisputes = await _context.Disputes
            .CountAsync(d => d.Lesson != null && d.Lesson.Tutorid == tutorId && d.Status != DisputeStatus.Resolved && d.Status != DisputeStatus.Closed);

        var tutorProfile = await _context.Tutorprofiles.FirstOrDefaultAsync(t => t.Tutorid == tutorId);

        var nextLessonEntities = await _context.Lessons
            .AsNoTracking()
            .Where(l => l.Tutorid == tutorId && l.Status == Scheduled && l.Scheduledstart > now)
            .OrderBy(l => l.Scheduledstart)
            .Take(5)
            .Include(l => l.Booking)
                .ThenInclude(b => b!.Tutorsubjectgradeprice)
                    .ThenInclude(p => p!.Subject)
            .Include(l => l.Booking)
                .ThenInclude(b => b!.Student)
            .ToListAsync();

        var nextLessons = nextLessonEntities.Select(l => new UpcomingLessonResponse
        {
            LessonId = l.Lessonid,
            BookingId = l.Bookingid,
            ScheduledStart = l.Scheduledstart,
            ScheduledEnd = l.Scheduledend,
            StudentName = l.Booking?.Student?.Fullname,
            SubjectName = l.Booking?.Subject?.Subjectname,
            MeetingLink = l.Meetinglink
        }).ToList();

        var hasVerifiedCerts = await _context.Tutorcertificates
            .AnyAsync(c => c.Tutorid == tutorId &&
                c.Verificationstatus != null &&
                c.Verificationstatus.ToLower() == CertificateStatus.Verified);

        // Compute missing fields when profile is not yet active
        List<string>? missingFields = null;
        if (tutorProfile != null && tutorProfile.Profilestatus != TutorProfileStatus.Active)
        {
            var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Userid == tutorId);
            var subjects = await _context.Tutorsubjectgradeprices.Where(s => s.Tutorid == tutorId && s.Isactive).ToListAsync();

            missingFields = new List<string>();
            if (string.IsNullOrWhiteSpace(tutorProfile.Headline)) missingFields.Add("headline");
            if (string.IsNullOrWhiteSpace(tutorProfile.Teachingareacity)) missingFields.Add("teachingArea");
            if (subjects == null || subjects.Count == 0) missingFields.Add("subjects");
            if (string.IsNullOrWhiteSpace(tutorProfile.Bio)) missingFields.Add("bio");
            if (string.IsNullOrWhiteSpace(tutorProfile.Education)) missingFields.Add("education");
            if (!subjects.Any(s => s.Isactive && s.Priceperhour > 0)) missingFields.Add("hourlyRate");
            if (user == null || string.IsNullOrWhiteSpace(user.Avatarurl)) missingFields.Add("avatar");
            if (string.IsNullOrWhiteSpace(tutorProfile.Videointrourl)) missingFields.Add("video");
            if (!hasVerifiedCerts) missingFields.Add("certificates");
            if (!(user?.Isidentityverified ?? false)) missingFields.Add("identity");
        }

        return new TutorDashboardStatsResponse
        {
            UpcomingLessons = upcomingCount,
            CompletedThisMonth = completedThisMonth,
            TotalCompleted = totalCompleted,
            EarningsThisMonth = earningsThisMonth,
            TotalEarnings = totalEarnings,
            WalletBalance = wallet?.Balance ?? 0,
            FrozenBalance = wallet?.Frozenbalance ?? 0,
            PendingConfirmation = pendingConfirmation,
            ActiveDisputes = activeDisputes,
            AverageRating = tutorProfile?.Averagerating ?? 0,
            TotalReviews = tutorProfile?.Totalreviews ?? 0,
            NextLessons = nextLessons,
            ProfileStatus = tutorProfile?.Profilestatus,
            HasVerifiedCertificates = hasVerifiedCerts,
            MissingFields = missingFields
        };
    }

    public async Task<LessonDetailResponse?> GetTutorLessonDetailAsync(int lessonId, string tutorId)
    {
        var lesson = await _context.Lessons
            .AsNoTracking()
            .Where(l => l.Lessonid == lessonId && l.Tutorid == tutorId)
            .Include(l => l.Booking)
                .ThenInclude(b => b!.Tutorsubjectgradeprice)
                    .ThenInclude(p => p!.Subject)
            .Include(l => l.Booking)
                .ThenInclude(b => b!.Student)
            .Include(l => l.Tutor)
                .ThenInclude(t => t!.Tutor)
            .Include(l => l.Lessonreport)
            .FirstOrDefaultAsync();

        if (lesson == null) return null;
        return MapToLessonDetailResponse(lesson);
    }
}
