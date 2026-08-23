using Microsoft.EntityFrameworkCore;
using MV.ApplicationLayer.Helpers;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Helpers;
using static MV.DomainLayer.Constants.ClassSessionStatus;
namespace MV.ApplicationLayer.Services;

public partial class ClassSessionService
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

        var classSessions = await _context.ClassSessions
            .AsNoTracking()
            .Where(l => l.Tutorid == tutorId && l.Scheduledstart >= startUtc && l.Scheduledstart <= endUtc)
            .Include(l => l.Booking)
                .ThenInclude(b => b!.Tutorsubjectgradeprice)
                    .ThenInclude(p => p!.Subject)
            .Include(l => l.Booking)
                .ThenInclude(b => b!.Student)
            .Include(l => l.RescheduleProposals)
            .OrderBy(l => l.Scheduledstart)
            .ToListAsync();

        // Group theo NGÀY Việt Nam để tránh lệch ngày do UTC+7
        var grouped = classSessions
            .GroupBy(l => l.Scheduledstart.Date)
            .Select(g => new CalendarDayResponse
            {
                Date = g.Key,
                ClassSessions = g.Select(l => new CalendarClassSessionResponse
                {
                    ClassSessionId = l.Classsessionid,
                    BookingId = l.Bookingid,
                    ScheduledStart = l.Scheduledstart,
                    ScheduledEnd = l.Scheduledend,
                    StudentName = l.Booking?.Student?.Fullname,
                    SubjectName = l.Booking?.Subject?.Subjectname,
                    Status = l.Status,
                    BookingStatus = l.Booking?.Status,
                    MeetingLink = l.Meetinglink,
                    CheckOutTime = l.Checkouttime,
                    HasRecording = RecordingStatusResolver.Resolve(l.Recordingurl, l.Recordings3key, l.Recordingsid, l.Checkouttime.HasValue).Status == "available",
                    HasPendingReschedule = ResolveHasPendingReschedule(l.RescheduleProposals),
                    IsContinuation = l.Iscontinuation,
                    IsDisputeRelearn = l.Isdisputerelearn,
                    OriginalClassSessionId = l.Originalsessionid,
                    SkipConfirmedByBothSides = l.Tutorskipconfirmedat.HasValue && l.Studentskipconfirmedat.HasValue
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

        var classSessions = await _context.ClassSessions
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
            .Include(l => l.ScheduleChanges)
            .Include(l => l.RescheduleProposals)
            .OrderBy(l => l.Scheduledstart)
            .ToListAsync();

        return classSessions
            .GroupBy(l => l.Scheduledstart.Date)
            .Select(g => new CalendarDayResponse
            {
                Date = g.Key,
                ClassSessions = g.Select(l => new CalendarClassSessionResponse
                {
                    ClassSessionId = l.Classsessionid,
                    BookingId = l.Bookingid,
                    ScheduledStart = l.Scheduledstart,
                    ScheduledEnd = l.Scheduledend,
                    TutorName = l.Booking?.Tutor?.Tutor?.Fullname,
                    SubjectName = l.Booking?.Subject?.Subjectname,
                    Status = l.Status,
                    BookingStatus = l.Booking?.Status,
                    MeetingLink = l.Meetinglink,
                    CheckOutTime = l.Checkouttime,
                    HasRecording = RecordingStatusResolver.Resolve(l.Recordingurl, l.Recordings3key, l.Recordingsid, l.Checkouttime.HasValue).Status == "available",
                    ScheduleChangeStatus = ResolveActiveScheduleChangeStatus(l.ScheduleChanges),
                    HasPendingReschedule = ResolveHasPendingReschedule(l.RescheduleProposals),
                    IsContinuation = l.Iscontinuation,
                    IsDisputeRelearn = l.Isdisputerelearn,
                    OriginalClassSessionId = l.Originalsessionid,
                    SkipConfirmedByBothSides = l.Tutorskipconfirmedat.HasValue && l.Studentskipconfirmedat.HasValue
                }).ToList()
            })
            .ToList();
    }

    public async Task<CalendarClassSessionResponse?> GetStudentNextClassSessionAsync(string studentUserId)
    {
        // Resolve studentId từ studentId hoặc linkedUserId (account tự đăng ký)
        var profile = await _context.Studentprofiles
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Studentid == studentUserId || s.Linkeduserid == studentUserId);

        if (profile == null)
            return null;

        var now = TimeZoneHelper.UtcNow;

        // Buổi in_progress luôn lấy dù đã quá giờ (gia sư có thể chưa check-out);
        // buổi scheduled chỉ lấy khi chưa kết thúc. Trạng thái `reserved` (chờ
        // thanh toán đợt 2) và booking đã hủy/hết hạn đều bị loại.
        var session = await _context.ClassSessions
            .AsNoTracking()
            .Where(l => l.Studentid == profile.Studentid
                     && l.Checkouttime == null
                     && ((l.Status == Scheduled && l.Scheduledend >= now)
                         || l.Status == InProgress)
                     && l.Booking != null
                     && l.Booking.Status != BookingStatus.Cancelled
                     && l.Booking.Status != BookingStatus.CancelledNoshow
                     && l.Booking.Status != BookingStatus.PaymentTimeout)
            .Include(l => l.Booking)
                .ThenInclude(b => b!.Tutorsubjectgradeprice)
                    .ThenInclude(p => p!.Subject)
            .Include(l => l.Booking)
                .ThenInclude(b => b!.Tutor)
                    .ThenInclude(t => t!.Tutor)
            .Include(l => l.ScheduleChanges)
            .Include(l => l.RescheduleProposals)
            // Buổi đang diễn ra lên đầu, phần còn lại theo giờ bắt đầu tăng dần.
            .OrderByDescending(l => l.Status == InProgress)
            .ThenBy(l => l.Scheduledstart)
            .FirstOrDefaultAsync();

        if (session == null)
            return null;

        // Buổi bị khoá vì chưa thanh toán đợt 2 thì không phải buổi học được.
        if (await IsSessionBlockedByRemainingPaymentAsync(session.Classsessionid))
            return null;

        return new CalendarClassSessionResponse
        {
            ClassSessionId = session.Classsessionid,
            BookingId = session.Bookingid,
            ScheduledStart = session.Scheduledstart,
            ScheduledEnd = session.Scheduledend,
            TutorName = session.Booking?.Tutor?.Tutor?.Fullname,
            SubjectName = session.Booking?.Subject?.Subjectname,
            Status = session.Status,
            BookingStatus = session.Booking?.Status,
            MeetingLink = session.Meetinglink,
            CheckOutTime = session.Checkouttime,
            HasRecording = RecordingStatusResolver.Resolve(session.Recordingurl, session.Recordings3key, session.Recordingsid, session.Checkouttime.HasValue).Status == "available",
            ScheduleChangeStatus = ResolveActiveScheduleChangeStatus(session.ScheduleChanges),
            HasPendingReschedule = ResolveHasPendingReschedule(session.RescheduleProposals),
            IsContinuation = session.Iscontinuation,
            IsDisputeRelearn = session.Isdisputerelearn,
            OriginalClassSessionId = session.Originalsessionid,
            SkipConfirmedByBothSides = session.Tutorskipconfirmedat.HasValue && session.Studentskipconfirmedat.HasValue
        };
    }

    public async Task<TutorDashboardStatsResponse> GetTutorDashboardStatsAsync(string tutorId)
    {
        var now = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
        var startOfMonth = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        var upcomingCount = await _context.ClassSessions
            .CountAsync(l => l.Tutorid == tutorId && l.Status == Scheduled && l.Scheduledstart > now);

        var completedThisMonth = await _context.ClassSessions
            .CountAsync(l => l.Tutorid == tutorId && l.Status == Completed && l.Scheduledstart >= startOfMonth);

        var totalCompleted = await _context.ClassSessions
            .CountAsync(l => l.Tutorid == tutorId && l.Status == Completed);

        // Tiền đã thực sự giải ngân về ví trong tháng (chỉ buổi đã quyết toán).
        var earningsThisMonth = await _context.ClassSessions
            .Where(l => l.Tutorid == tutorId && l.Status == Completed && l.Issettled == true && l.Scheduledstart >= startOfMonth)
            .SumAsync(l => l.Lessonprice ?? 0);

        // Buổi đã dạy trong tháng nhưng tiền còn bị giữ
        var earnedPendingThisMonth = await _context.ClassSessions
            .Where(l => l.Tutorid == tutorId
                && l.Scheduledstart >= startOfMonth
                && l.Issettled != true
                && (l.Status == Completed
                    || l.Status == PendingConfirmation
                    || l.Status == InProgress))
            .SumAsync(l => l.Lessonprice ?? 0);

        var totalEarnings = await _context.ClassSessions
            .Where(l => l.Tutorid == tutorId && l.Status == Completed && l.Issettled == true)
            .SumAsync(l => l.Lessonprice ?? 0);

        var wallet = await _context.Wallets.FirstOrDefaultAsync(w => w.Userid == tutorId);

        var pendingConfirmation = await _context.ClassSessions
            .CountAsync(l => l.Tutorid == tutorId && l.Status == PendingConfirmation);

        var activeDisputes = await _context.Disputes
            .CountAsync(d => d.ClassSession != null && d.ClassSession.Tutorid == tutorId && d.Status != DisputeStatus.Resolved && d.Status != DisputeStatus.Closed);

        // Tiền các buổi đã lên lịch nhưng chưa dạy
        var startOfNextMonth = startOfMonth.AddMonths(1);

        var upcomingThisMonthQuery = _context.ClassSessions
            .Where(l => l.Tutorid == tutorId
                && l.Status == Scheduled
                && l.Scheduledstart > now
                && l.Scheduledstart < startOfNextMonth);

        var upcomingEarnings = await upcomingThisMonthQuery.SumAsync(l => l.Lessonprice ?? 0);
        var upcomingThisMonthCount = await upcomingThisMonthQuery.CountAsync();

        var reportedSessionIds = _context.ClassSessionReports
            .Where(r => r.Classsessionid != null)
            .Select(r => r.Classsessionid!.Value);

        var awaitingReportQuery = _context.ClassSessions
            .Where(l => l.Tutorid == tutorId
                && l.Status == InProgress
                && l.Checkouttime != null
                && !reportedSessionIds.Contains(l.Classsessionid));

        var awaitingReportCount = await awaitingReportQuery.CountAsync();

        var awaitingReportEntities = await awaitingReportQuery
            .AsNoTracking()
            .OrderBy(l => l.Scheduledstart)
            .Take(5)
            .Include(l => l.Booking)
                .ThenInclude(b => b!.Tutorsubjectgradeprice)
                    .ThenInclude(p => p!.Subject)
            .Include(l => l.Booking)
                .ThenInclude(b => b!.Student)
            .ToListAsync();

        var awaitingReportClassSessions = awaitingReportEntities.Select(l => new AwaitingReportClassSessionResponse
        {
            ClassSessionId = l.Classsessionid,
            BookingId = l.Bookingid,
            ScheduledStart = l.Scheduledstart,
            ScheduledEnd = l.Scheduledend,
            StudentName = l.Booking?.Student?.Fullname,
            SubjectName = l.Booking?.Subject?.Subjectname,
            CheckOutTime = l.Checkouttime,
            ClassSessionPrice = l.Lessonprice ?? 0
        }).ToList();

        var tutorProfile = await _context.Tutorprofiles.FirstOrDefaultAsync(t => t.Tutorid == tutorId);

        var nextClassSessionEntities = await _context.ClassSessions
            .AsNoTracking()
            .Where(l => l.Tutorid == tutorId && l.Status == Scheduled && l.Scheduledstart > now)
            .OrderBy(l => l.Scheduledstart)
            .Take(20)
            .Include(l => l.Booking)
                .ThenInclude(b => b!.Tutorsubjectgradeprice)
                    .ThenInclude(p => p!.Subject)
            .Include(l => l.Booking)
                .ThenInclude(b => b!.Student)
            .ToListAsync();

        var nextClassSessions = nextClassSessionEntities.Select(l => new UpcomingClassSessionResponse
        {
            ClassSessionId = l.Classsessionid,
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

        var walletBalance = wallet?.Balance ?? 0;
        var frozenBalance = wallet?.Frozenbalance ?? 0;

        return new TutorDashboardStatsResponse
        {
            UpcomingClassSessions = upcomingCount,
            CompletedThisMonth = completedThisMonth,
            TotalCompleted = totalCompleted,
            EarningsThisMonth = earningsThisMonth,
            EarnedPendingThisMonth = earnedPendingThisMonth,
            TotalEarnings = totalEarnings,
            WalletBalance = walletBalance,
            AvailableBalance = walletBalance,
            FrozenBalance = frozenBalance,
            TotalBalance = walletBalance + frozenBalance,
            PendingConfirmation = pendingConfirmation,
            UpcomingEarnings = upcomingEarnings,
            UpcomingClassSessionsThisMonth = upcomingThisMonthCount,
            AwaitingReport = awaitingReportCount,
            AwaitingReportClassSessions = awaitingReportClassSessions,
            ActiveDisputes = activeDisputes,
            AverageRating = tutorProfile?.Averagerating ?? 0,
            TotalReviews = tutorProfile?.Totalreviews ?? 0,
            NextClassSessions = nextClassSessions,
            ProfileStatus = tutorProfile?.Profilestatus,
            HasVerifiedCertificates = hasVerifiedCerts,
            MissingFields = missingFields
        };
    }

    public async Task<ClassSessionDetailResponse?> GetTutorClassSessionDetailAsync(int classSessionId, string tutorId)
    {
        var classSession = await _context.ClassSessions
            .AsNoTracking()
            .Where(l => l.Classsessionid == classSessionId && l.Tutorid == tutorId)
            .Include(l => l.Booking)
                .ThenInclude(b => b!.Tutorsubjectgradeprice)
                    .ThenInclude(p => p!.Subject)
            .Include(l => l.Booking)
                .ThenInclude(b => b!.Student)
            .Include(l => l.Tutor)
                .ThenInclude(t => t!.Tutor)
            .Include(l => l.ClassSessionReport)
            .Include(l => l.InterruptedbyNavigation)
            .FirstOrDefaultAsync();

        if (classSession == null) return null;
        var response = MapToClassSessionDetailResponse(classSession);

        var rescheduleProposals = await _rescheduleProposalService.GetProposalHistoryAsync(classSessionId);
        response.RescheduleProposals = rescheduleProposals;
        response.PendingRescheduleProposal = rescheduleProposals
            .FirstOrDefault(x => x.Status == RescheduleProposalStatus.Pending);

        // Buổi bị ngắt (status=interrupted) không bao giờ tự quay lại in_progress được nữa — cần
        // biết buổi phụ tương ứng đã được cả 2 phía đồng ý bỏ chưa để hiện đúng nút "Nộp báo cáo"
        // (xem ContinuationSkipBothConfirmed/CanSubmitReport, SubmitReportAsync). Chỉ lấy buổi phụ
        // khi nó CÒN Scheduled — khớp đúng guard trong ConfirmSkipContinuationAsync: buổi phụ đã
        // được vào học (InProgress/Completed/...) thì việc "bỏ buổi phụ" không còn ý nghĩa nữa,
        // không được để FE tiếp tục hiện lựa chọn bỏ buổi.
        if (classSession.Status == Interrupted)
        {
            var continuation = await _context.ClassSessions
                .AsNoTracking()
                .Where(c => c.Originalsessionid == classSessionId && c.Iscontinuation && c.Status == Scheduled)
                .Select(c => new { c.Classsessionid, c.Tutorskipconfirmedat, c.Studentskipconfirmedat })
                .FirstOrDefaultAsync();
            if (continuation != null)
            {
                response.ContinuationSessionId = continuation.Classsessionid;
                response.ContinuationSkipBothConfirmed =
                    continuation.Tutorskipconfirmedat.HasValue && continuation.Studentskipconfirmedat.HasValue;
            }
        }

        return response;
    }
}
