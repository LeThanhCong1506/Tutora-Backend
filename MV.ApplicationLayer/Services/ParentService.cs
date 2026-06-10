using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Entities;
using MV.DomainLayer.Exceptions;
using MV.DomainLayer.Helpers;
using MV.ApplicationLayer.Interfaces;
using System.Text.Json;
using static MV.DomainLayer.Constants.LessonStatus;
namespace MV.ApplicationLayer.Services;

/// <summary>
/// Service for parent lesson management - confirm, dispute
/// </summary>
public class ParentService : IParentService
{
    private readonly IAppDbContext _context;
    private readonly ISettlementService _settlementService;
    private readonly INotificationService _notificationService;
    private readonly ILogger<ParentService> _logger;

    public ParentService(
        IAppDbContext context,
        ISettlementService settlementService,
        INotificationService notificationService,
        ILogger<ParentService> logger)
    {
        _context = context;
        _settlementService = settlementService;
        _notificationService = notificationService;
        _logger = logger;
    }

    /// <summary>
    /// Get lessons pending parent confirmation
    /// </summary>
    public async Task<List<PendingLessonResponse>> GetPendingLessonsAsync(string userId, string role)
    {
        var studentIds = role == UserRole.Parent
            ? await _context.Studentprofiles.Where(s => s.Parentid == userId).Select(s => s.Studentid).ToListAsync()
            : await _context.Studentprofiles.Where(s => s.Studentid == userId || s.Linkeduserid == userId).Select(s => s.Studentid).ToListAsync();

        // Load entity trước, project sang DTO trong memory để dùng được MV.DomainLayer.Helpers.TimeZoneHelper.ToUserTime()
        var lessons = await _context.Lessons
            .AsNoTracking()
            .Where(l => l.Status == PendingConfirmation &&
                        studentIds.Contains(l.Studentid!) &&
                        l.Issettled != true)
            .Include(l => l.Booking)
                .ThenInclude(b => b!.Tutorsubjectgradeprice)
                    .ThenInclude(p => p!.Subject)
            .Include(l => l.Booking)
                .ThenInclude(b => b!.Student)
                    .ThenInclude(s => s!.GradelevelNavigation)
            .Include(l => l.Tutor)
                .ThenInclude(t => t!.Tutor)
            .OrderBy(l => l.Confirmdeadline)
            .ToListAsync();

        return lessons.Select(l => new PendingLessonResponse
        {
            LessonId = l.Lessonid,
            BookingId = l.Bookingid,
            ScheduledStart = MV.DomainLayer.Helpers.TimeZoneHelper.ToUserTime(l.Scheduledstart),
            ScheduledEnd = MV.DomainLayer.Helpers.TimeZoneHelper.ToUserTime(l.Scheduledend),
            SubmittedAt = MV.DomainLayer.Helpers.TimeZoneHelper.ToUserTime(l.Submittedat),
            ConfirmDeadline = MV.DomainLayer.Helpers.TimeZoneHelper.ToUserTime(l.Confirmdeadline),
            TutorName = l.Tutor?.Tutor?.Fullname,
            TutorAvatarUrl = l.Tutor?.Tutor?.Avatarurl,
            StudentName = l.Booking?.Student?.Fullname,
            SubjectName = l.Booking?.Subject?.Subjectname,
            LessonPrice = l.Lessonprice,
            LessonContent = l.Lessoncontent,
            Homework = l.Homework,
            TutorNotes = l.Tutornotes
        }).ToList();
    }

    /// <summary>
    /// Get lesson detail for parent view
    /// </summary>
    public async Task<LessonDetailResponse?> GetLessonDetailAsync(int lessonId, string userId, string role)
    {
        var studentIds = role == UserRole.Parent
            ? await _context.Studentprofiles.Where(s => s.Parentid == userId).Select(s => s.Studentid).ToListAsync()
            : await _context.Studentprofiles.Where(s => s.Studentid == userId || s.Linkeduserid == userId).Select(s => s.Studentid).ToListAsync();

        var lesson = await _context.Lessons
            .AsNoTracking()
            .Where(l => l.Lessonid == lessonId && studentIds.Contains(l.Studentid!))
            .Include(l => l.Booking)
                .ThenInclude(b => b!.Tutorsubjectgradeprice)
                    .ThenInclude(p => p!.Subject)
            .Include(l => l.Booking)
                .ThenInclude(b => b!.Student)
                    .ThenInclude(s => s!.GradelevelNavigation)
            .Include(l => l.Tutor)
                .ThenInclude(t => t!.Tutor)
            .Include(l => l.Lessonreport)
            .FirstOrDefaultAsync();

        if (lesson == null) return null;

        return new LessonDetailResponse
        {
            LessonId = lesson.Lessonid,
            BookingId = lesson.Bookingid,
            // Tất cả datetime trả về giờ Việt Nam (UTC+7) để frontend hiển thị đúng
            ScheduledStart = MV.DomainLayer.Helpers.TimeZoneHelper.ToUserTime(lesson.Scheduledstart),
            ScheduledEnd = MV.DomainLayer.Helpers.TimeZoneHelper.ToUserTime(lesson.Scheduledend),
            RealStart = MV.DomainLayer.Helpers.TimeZoneHelper.ToUserTime(lesson.Realstart),
            RealEnd = MV.DomainLayer.Helpers.TimeZoneHelper.ToUserTime(lesson.Realend),
            CheckInTime = MV.DomainLayer.Helpers.TimeZoneHelper.ToUserTime(lesson.Checkintime),
            CheckOutTime = MV.DomainLayer.Helpers.TimeZoneHelper.ToUserTime(lesson.Checkouttime),
            IsTutorPresent = lesson.Istutorpresent,
            IsStudentPresent = lesson.Isstudentpresent,
            AttendanceNote = lesson.Attendancenote,
            Status = lesson.Status,
            SubmittedAt = MV.DomainLayer.Helpers.TimeZoneHelper.ToUserTime(lesson.Submittedat),
            ConfirmDeadline = MV.DomainLayer.Helpers.TimeZoneHelper.ToUserTime(lesson.Confirmdeadline),
            ParentAckAt = MV.DomainLayer.Helpers.TimeZoneHelper.ToUserTime(lesson.Parentackat),
            IsSettled = lesson.Issettled,
            LessonContent = lesson.Lessoncontent,
            Homework = lesson.Homework,
            TutorNotes = lesson.Tutornotes,
            MeetingLink = lesson.Meetinglink,
            LessonPrice = lesson.Lessonprice,
            Student = lesson.Booking?.Student != null ? new LessonStudentResponse
            {
                StudentId = lesson.Booking.Student.Studentid,
                FullName = lesson.Booking.Student.Fullname,
                School = lesson.Booking.Student.School,
                GradeLevel = lesson.Booking.Student.Gradelevel
            } : null,
            Tutor = lesson.Tutor?.Tutor != null ? new LessonTutorResponse
            {
                TutorId = lesson.Tutor.Tutorid,
                FullName = lesson.Tutor.Tutor.Fullname,
                AvatarUrl = lesson.Tutor.Tutor.Avatarurl,
                AverageRating = lesson.Tutor.Averagerating
            } : null,
            Subject = lesson.Booking?.Tutorsubjectgradeprice?.Subject != null ? new LessonSubjectResponse
            {
                SubjectId = lesson.Booking.Tutorsubjectgradeprice.Subject.Subjectid,
                SubjectName = lesson.Booking.Tutorsubjectgradeprice.Subject.Subjectname
            } : null,
            Report = lesson.Lessonreport != null ? new LessonReportResponse
            {
                ReportId = lesson.Lessonreport.Reportid,
                ContentCovered = lesson.Lessonreport.Contentcovered,
                HomeworkAssigned = lesson.Lessonreport.Homeworkassigned,
                StudentPerformanceRating = lesson.Lessonreport.Studentperformancerating,
                Attachments = DeserializeJsonList(lesson.Lessonreport.Attachments),
                CreatedAt = lesson.Lessonreport.Createdat.HasValue ? MV.DomainLayer.Helpers.TimeZoneHelper.ToUserTime(lesson.Lessonreport.Createdat.Value) : (DateTime?)null
            } : null
        };
    }

    /// <summary>
    /// Confirm a lesson as completed (triggers settlement)
    /// </summary>
    public async Task<SettlementResultResponse> ConfirmLessonAsync(int lessonId, string userId, string role)
    {
        var studentIds = role == UserRole.Parent
            ? await _context.Studentprofiles.Where(s => s.Parentid == userId).Select(s => s.Studentid).ToListAsync()
            : await _context.Studentprofiles.Where(s => s.Studentid == userId || s.Linkeduserid == userId).Select(s => s.Studentid).ToListAsync();

        var lesson = await _context.Lessons
            .Include(l => l.Booking)
            .FirstOrDefaultAsync(l => l.Lessonid == lessonId && studentIds.Contains(l.Studentid!))
            ?? throw new LessonException(LessonErrorCodes.LessonNotFound, "Không tìm thấy buổi học hoặc bạn không có quyền truy cập", 404);

        if (lesson.Status != PendingConfirmation)
            throw new LessonException(LessonErrorCodes.InvalidLessonStatus, "Buổi học không ở trạng thái chờ xác nhận", 400);

        if (lesson.Issettled == true)
            throw new LessonException(LessonErrorCodes.LessonAlreadyConfirmed, "Buổi học đã được xác nhận rồi", 400);

        // Check if lesson has an active dispute
        var hasDispute = await _context.Disputes
            .AnyAsync(d => d.Lessonid == lessonId && d.Status != DisputeStatus.Resolved && d.Status != DisputeStatus.Closed);
        if (hasDispute)
            throw new LessonException(LessonErrorCodes.DisputeAlreadyExists, "Không thể xác nhận buổi học khi đang có tranh chấp", 400);

        _logger.LogInformation("User {UserId} ({Role}) confirming lesson {LessonId}", userId, role, lessonId);

        return await _settlementService.SettleLessonAsync(lessonId, userId);
    }

    /// <summary>
    /// Create a dispute for a lesson
    /// </summary>
    public async Task<DisputeDetailResponse> CreateDisputeAsync(int lessonId, string userId, string role, CreateDisputeRequest request)
    {
        var studentIds = role == UserRole.Parent
            ? await _context.Studentprofiles.Where(s => s.Parentid == userId).Select(s => s.Studentid).ToListAsync()
            : await _context.Studentprofiles.Where(s => s.Studentid == userId || s.Linkeduserid == userId).Select(s => s.Studentid).ToListAsync();

        var lesson = await _context.Lessons
            .Include(l => l.Booking)
            .Include(l => l.Disputes)
            .FirstOrDefaultAsync(l => l.Lessonid == lessonId && studentIds.Contains(l.Studentid!))
            ?? throw new LessonException(LessonErrorCodes.LessonNotFound, "Không tìm thấy buổi học hoặc bạn không có quyền truy cập", 404);

        if (lesson.Status != PendingConfirmation && lesson.Status != Completed)
            throw new LessonException(LessonErrorCodes.InvalidLessonStatus, "Buổi học không thể tạo tranh chấp ở trạng thái này", 400);

        if (lesson.Disputes.Any())
            throw new LessonException(LessonErrorCodes.DisputeAlreadyExists, "Buổi học này đã có tranh chấp rồi", 400);

        if (!DisputeTypes.All.Contains(request.DisputeType))
            throw new ArgumentException("Loại tranh chấp không hợp lệ");

        var dispute = new Dispute
        {
            Lessonid = lessonId,
            Bookingid = lesson.Bookingid,
            Createdby = userId,
            Disputetype = request.DisputeType,
            Reason = request.Reason,
            Status = DisputeStatus.Pending,
            Evidence = request.Evidence != null ? JsonSerializer.Serialize(request.Evidence) : null,
            Createdat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow
        };

        _context.Disputes.Add(dispute);

        // Update lesson status
        lesson.Status = Disputed;

        await _context.SaveChangesAsync();

        _logger.LogInformation("{Role} {UserId} created dispute {DisputeId} for lesson {LessonId}",
            role, userId, dispute.Disputeid, lessonId);

        // Notify Admins
        var admins = await _context.Users
            .Where(u => u.Primaryrole == UserRole.Admin)
            .Select(u => u.Userid)
            .ToListAsync();

        if (admins.Any())
        {
            var notis = admins.Select(adminId => new NotificationRequest
            {
                Userid = adminId,
                Title = "Tranh chấp mới",
                Message = $"Phụ huynh đã tạo tranh chấp cho buổi học #{lessonId}. Lý do: {request.Reason}"
            });
            await _notificationService.CreateNotificationsAsync(notis);
        }

        return new DisputeDetailResponse
        {
            DisputeId = dispute.Disputeid,
            BookingId = dispute.Bookingid,
            LessonId = dispute.Lessonid,
            DisputeType = request.DisputeType,
            Reason = dispute.Reason,
            Status = dispute.Status,
            Evidence = request.Evidence,
            CreatedAt = dispute.Createdat.HasValue ? MV.DomainLayer.Helpers.TimeZoneHelper.ToUserTime(dispute.Createdat.Value) : (DateTime?)null,
            CreatedBy = new DisputeUserResponse
            {
                UserId = userId
            }
        };
    }

    /// <summary>
    /// Get parent's dispute history
    /// </summary>
    public async Task<PagedList<DisputeListResponse>> GetParentDisputesAsync(string userId, string role, int page, int pageSize)
    {
        var query = _context.Disputes
            .AsNoTracking()
            .Where(d => d.Createdby == userId)
            .OrderByDescending(d => d.Createdat);

        var totalCount = await query.CountAsync();

        var rawDisputes = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Include(d => d.Lesson)
                .ThenInclude(l => l!.Tutor)
                    .ThenInclude(t => t!.Tutor)
            .Select(d => new
            {
                d.Disputeid,
                d.Lessonid,
                d.Bookingid,
                d.Status,
                d.Reason,
                TutorName = d.Lesson!.Tutor!.Tutor!.Fullname,
                LessonPrice = d.Lesson.Lessonprice,
                d.Createdat
            })
            .ToListAsync();

        var disputes = rawDisputes.Select(d => new DisputeListResponse
        {
            DisputeId = d.Disputeid,
            LessonId = d.Lessonid,
            BookingId = d.Bookingid,
            Status = d.Status,
            Reason = d.Reason,
            TutorName = d.TutorName,
            LessonPrice = d.LessonPrice,
            CreatedAt = d.Createdat.HasValue ? MV.DomainLayer.Helpers.TimeZoneHelper.ToUserTime(d.Createdat.Value) : (DateTime?)null
        }).ToList();

        return new PagedList<DisputeListResponse>(disputes, totalCount, page, pageSize);
    }

    /// <summary>
    /// Get parent calendar view
    /// </summary>
    public async Task<List<CalendarDayResponse>> GetParentCalendarAsync(string userId, string role, DateTime startDate, DateTime endDate)
    {
        try
        {
            // Normalize timezone: nếu frontend gửi UTC thì giữ nguyên, nếu Unspecified thì coi như user time
            var startUtc = startDate.Kind == DateTimeKind.Utc 
                ? startDate 
                : TimeZoneHelper.ToUtc(startDate);
            var endUtc = endDate.Kind == DateTimeKind.Utc 
                ? endDate 
                : TimeZoneHelper.ToUtc(endDate);

            var studentIds = role == UserRole.Parent
                ? await _context.Studentprofiles.Where(s => s.Parentid == userId).Select(s => s.Studentid).ToListAsync()
                : await _context.Studentprofiles.Where(s => s.Studentid == userId || s.Linkeduserid == userId).Select(s => s.Studentid).ToListAsync();

            if (studentIds == null || studentIds.Count == 0)
                return new List<CalendarDayResponse>();

            var lessons = await _context.Lessons
                .AsNoTracking()
                .Where(l => l.Studentid != null && studentIds.Contains(l.Studentid) && l.Scheduledstart >= startUtc && l.Scheduledstart <= endUtc)
                .Include(l => l.Booking)
                    .ThenInclude(b => b!.Tutorsubjectgradeprice)
                        .ThenInclude(p => p!.Subject)
                .Include(l => l.Booking)
                    .ThenInclude(b => b!.Student)
                .Include(l => l.Booking)
                    .ThenInclude(b => b!.Tutor)
                        .ThenInclude(t => t!.Tutor)
                .OrderBy(l => l.Scheduledstart)
                .ToListAsync();

            return lessons
                .GroupBy(l => MV.DomainLayer.Helpers.TimeZoneHelper.ToUserTime(l.Scheduledstart).Date)
                .Select(g => new CalendarDayResponse
                {
                    Date = g.Key,
                    Lessons = g.Select(l => new CalendarLessonResponse
                    {
                        LessonId = l.Lessonid,
                        ScheduledStart = MV.DomainLayer.Helpers.TimeZoneHelper.ToUserTime(l.Scheduledstart),
                        ScheduledEnd = MV.DomainLayer.Helpers.TimeZoneHelper.ToUserTime(l.Scheduledend),
                        StudentName = l.Booking?.Student?.Fullname,
                        TutorName = l.Booking?.Tutor?.Tutor?.Fullname,
                        SubjectName = l.Booking?.Subject?.Subjectname,
                        Status = l.Status,
                        MeetingLink = l.Meetinglink
                    }).ToList()
                })
                .ToList();
        }
        catch (Exception ex)
        {
            throw new Exception($"Error getting parent calendar: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Deserialize JSON array or legacy comma-separated string
    /// </summary>
    private static List<string>? DeserializeJsonList(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        if (value.TrimStart().StartsWith('['))
        {
            try { return JsonSerializer.Deserialize<List<string>>(value); }
            catch { }
        }

        return value.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
    }
}
