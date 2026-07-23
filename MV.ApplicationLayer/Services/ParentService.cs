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
using static MV.DomainLayer.Constants.ClassSessionStatus;
using Microsoft.AspNetCore.Http;
namespace MV.ApplicationLayer.Services;

/// <summary>
/// Service for parent classSession management - confirm, dispute
/// </summary>
public class ParentService : IParentService
{
    private readonly IAppDbContext _context;
    private readonly ISettlementService _settlementService;
    private readonly INotificationService _notificationService;
    private readonly IFileStorageService _storageService;
    private readonly ILogger<ParentService> _logger;

    public ParentService(
        IAppDbContext context,
        ISettlementService settlementService,
        INotificationService notificationService,
        IFileStorageService storageService,
        ILogger<ParentService> logger)
    {
        _context = context;
        _settlementService = settlementService;
        _notificationService = notificationService;
        _storageService = storageService;
        _logger = logger;
    }

    /// <summary>
    /// Get classSessions pending parent confirmation
    /// </summary>
    public async Task<List<PendingClassSessionResponse>> GetPendingClassSessionsAsync(string userId, string role)
    {
        var studentIds = role == UserRole.Parent
            ? await _context.Studentprofiles.Where(s => s.Parentid == userId).Select(s => s.Studentid).ToListAsync()
            : await _context.Studentprofiles.Where(s => s.Studentid == userId || s.Linkeduserid == userId).Select(s => s.Studentid).ToListAsync();

        var classSessions = await _context.ClassSessions
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

        return classSessions.Select(l => new PendingClassSessionResponse
        {
            ClassSessionId = l.Classsessionid,
            BookingId = l.Bookingid,
            ScheduledStart = l.Scheduledstart,
            ScheduledEnd = l.Scheduledend,
            SubmittedAt = l.Submittedat,
            ConfirmDeadline = l.Confirmdeadline,
            TutorName = l.Tutor?.Tutor?.Fullname,
            TutorAvatarUrl = l.Tutor?.Tutor?.Avatarurl,
            StudentName = l.Booking?.Student?.Fullname,
            SubjectName = l.Booking?.Subject?.Subjectname,
            ClassSessionPrice = l.Lessonprice,
            ClassSessionContent = l.Lessoncontent,
            Homework = l.Homework,
            TutorNotes = l.Tutornotes
        }).ToList();
    }

    /// <summary>
    /// Get classSession detail for parent view
    /// </summary>
    public async Task<ClassSessionDetailResponse?> GetClassSessionDetailAsync(int classSessionId, string userId, string role)
    {
        var studentIds = role == UserRole.Parent
            ? await _context.Studentprofiles.Where(s => s.Parentid == userId).Select(s => s.Studentid).ToListAsync()
            : await _context.Studentprofiles.Where(s => s.Studentid == userId || s.Linkeduserid == userId).Select(s => s.Studentid).ToListAsync();

        var classSession = await _context.ClassSessions
            .AsNoTracking()
            .Where(l => l.Classsessionid == classSessionId && studentIds.Contains(l.Studentid!))
            .Include(l => l.Booking)
                .ThenInclude(b => b!.Tutorsubjectgradeprice)
                    .ThenInclude(p => p!.Subject)
            .Include(l => l.Booking)
                .ThenInclude(b => b!.Student)
                    .ThenInclude(s => s!.GradelevelNavigation)
            .Include(l => l.Tutor)
                .ThenInclude(t => t!.Tutor)
            .Include(l => l.ClassSessionReport)
            .FirstOrDefaultAsync();

        if (classSession == null) return null;

        // Buổi tiếp theo bị khóa nếu chưa thanh toán đợt 2 (các buổi còn lại).
        var requiresRemainingPayment = classSession.Booking != null
            && (classSession.Booking.Status == BookingStatus.DepositPaid
                || classSession.Booking.Status == BookingStatus.PendingRemainingPayment)
            && classSession.Booking.Remainingpaidat == null
            && await _context.ClassSessions.AnyAsync(
                l => l.Bookingid == classSession.Bookingid && l.Classsessionid != classSessionId
                && (l.Status == Completed || l.Status == PendingConfirmation || l.Status == InProgress));

        return new ClassSessionDetailResponse
        {
            ClassSessionId = classSession.Classsessionid,
            BookingId = classSession.Bookingid,
            // Tất cả datetime trả về giờ Việt Nam (UTC+7) để frontend hiển thị đúng
            ScheduledStart = classSession.Scheduledstart,
            ScheduledEnd = classSession.Scheduledend,
            RealStart = classSession.Realstart,
            RealEnd = classSession.Realend,
            CheckInTime = classSession.Checkintime,
            CheckOutTime = classSession.Checkouttime,
            IsTutorPresent = classSession.Istutorpresent,
            IsStudentPresent = classSession.Isstudentpresent,
            AttendanceNote = classSession.Attendancenote,
            Status = classSession.Status,
            SubmittedAt = classSession.Submittedat,
            ConfirmDeadline = classSession.Confirmdeadline,
            ParentAckAt = classSession.Parentackat,
            IsSettled = classSession.Issettled,
            ClassSessionContent = classSession.Lessoncontent,
            Homework = classSession.Homework,
            TutorNotes = classSession.Tutornotes,
            MeetingLink = classSession.Meetinglink,
            RequiresRemainingPayment = requiresRemainingPayment,
            ClassSessionPrice = classSession.Lessonprice,
            Student = classSession.Booking?.Student != null ? new ClassSessionStudentResponse
            {
                StudentId = classSession.Booking.Student.Studentid,
                FullName = classSession.Booking.Student.Fullname,
                School = classSession.Booking.Student.School,
                GradeLevel = classSession.Booking.Student.Gradelevel,
                AvatarUrl = classSession.Booking.Student.Avatarurl
            } : null,
            Tutor = classSession.Tutor?.Tutor != null ? new ClassSessionTutorResponse
            {
                TutorId = classSession.Tutor.Tutorid,
                FullName = classSession.Tutor.Tutor.Fullname,
                AvatarUrl = classSession.Tutor.Tutor.Avatarurl,
                AverageRating = classSession.Tutor.Averagerating
            } : null,
            Subject = classSession.Booking?.Tutorsubjectgradeprice?.Subject != null ? new ClassSessionSubjectResponse
            {
                SubjectId = classSession.Booking.Tutorsubjectgradeprice.Subject.Subjectid,
                SubjectName = classSession.Booking.Tutorsubjectgradeprice.Subject.Subjectname
            } : null,
            Report = classSession.ClassSessionReport != null ? new ClassSessionReportResponse
            {
                ReportId = classSession.ClassSessionReport.Reportid,
                ContentCovered = classSession.ClassSessionReport.Contentcovered,
                HomeworkAssigned = classSession.ClassSessionReport.Homeworkassigned,
                StudentPerformanceRating = classSession.ClassSessionReport.Studentperformancerating,
                Attachments = DeserializeJsonList(classSession.ClassSessionReport.Attachments),
                CreatedAt = classSession.ClassSessionReport.Createdat.HasValue ? classSession.ClassSessionReport.Createdat.Value : (DateTime?)null
            } : null
        };
    }

    /// <summary>
    /// Confirm a classSession as completed (triggers settlement)
    /// </summary>
    public async Task<SettlementResultResponse> ConfirmClassSessionAsync(int classSessionId, string userId, string role)
    {
        var studentIds = role == UserRole.Parent
            ? await _context.Studentprofiles.Where(s => s.Parentid == userId).Select(s => s.Studentid).ToListAsync()
            : await _context.Studentprofiles.Where(s => s.Studentid == userId || s.Linkeduserid == userId).Select(s => s.Studentid).ToListAsync();

        var classSession = await _context.ClassSessions
            .Include(l => l.Booking)
            .FirstOrDefaultAsync(l => l.Classsessionid == classSessionId && studentIds.Contains(l.Studentid!))
            ?? throw new ClassSessionException(ClassSessionErrorCodes.ClassSessionNotFound, "Không tìm thấy buổi học hoặc bạn không có quyền truy cập", 404);

        if (classSession.Status != PendingConfirmation)
            throw new ClassSessionException(ClassSessionErrorCodes.InvalidClassSessionStatus, "Buổi học không ở trạng thái chờ xác nhận", 400);

        if (classSession.Issettled == true)
            throw new ClassSessionException(ClassSessionErrorCodes.ClassSessionAlreadyConfirmed, "Buổi học đã được xác nhận rồi", 400);

        // Check if classSession has an active dispute
        var hasDispute = await _context.Disputes
            .AnyAsync(d => d.Classsessionid == classSessionId && d.Status != DisputeStatus.Resolved && d.Status != DisputeStatus.Closed);
        if (hasDispute)
            throw new ClassSessionException(ClassSessionErrorCodes.DisputeAlreadyExists, "Không thể xác nhận buổi học khi đang có tranh chấp", 400);

        _logger.LogInformation("User {UserId} ({Role}) confirming classSession {ClassSessionId}", userId, role, classSessionId);

        return await _settlementService.SettleClassSessionAsync(classSessionId, userId);
    }

    /// <summary>
    /// Create a dispute for a classSession
    /// </summary>
    public async Task<DisputeDetailResponse> CreateDisputeAsync(int classSessionId, string userId, string role, CreateDisputeRequest request)
    {
        var studentIds = role == UserRole.Parent
            ? await _context.Studentprofiles.Where(s => s.Parentid == userId).Select(s => s.Studentid).ToListAsync()
            : await _context.Studentprofiles.Where(s => s.Studentid == userId || s.Linkeduserid == userId).Select(s => s.Studentid).ToListAsync();

        var classSession = await _context.ClassSessions
            .Include(l => l.Booking)
            .Include(l => l.Disputes)
            .FirstOrDefaultAsync(l => l.Classsessionid == classSessionId && studentIds.Contains(l.Studentid!))
            ?? throw new ClassSessionException(ClassSessionErrorCodes.ClassSessionNotFound, "Không tìm thấy buổi học hoặc bạn không có quyền truy cập", 404);

        if (role == UserRole.Student)
        {
            var studentProfile = await _context.Studentprofiles.FirstOrDefaultAsync(s => s.Studentid == userId || s.Linkeduserid == userId);
            if (studentProfile != null && studentProfile.Parentid != null)
            {
                throw new ClassSessionException(BookingErrorCodes.StudentManagedByParent, "Tài khoản học sinh do phụ huynh quản lý không thể tự tạo tranh chấp", 403);
            }
        }

        // An already-settled classSession cannot be disputed: a refund would throw on Issettled and a
        // Release would throw on status → the dispute would deadlock (unresolvable).
        if (classSession.Issettled == true)
            throw new ClassSessionException(ClassSessionErrorCodes.ClassSessionAlreadyConfirmed, "Buổi học đã thanh toán, không thể tạo tranh chấp", 400);

        if (classSession.Status != PendingConfirmation && classSession.Status != Completed)
            throw new ClassSessionException(ClassSessionErrorCodes.InvalidClassSessionStatus, "Buổi học không thể tạo tranh chấp ở trạng thái này", 400);

        if (classSession.Disputes.Any())
            throw new ClassSessionException(ClassSessionErrorCodes.DisputeAlreadyExists, "Buổi học này đã có tranh chấp rồi", 400);

        if (!DisputeTypes.All.Contains(request.DisputeType))
            throw new ArgumentException("Loại tranh chấp không hợp lệ");

        var dispute = new Dispute
        {
            Classsessionid = classSessionId,
            Bookingid = classSession.Bookingid,
            Createdby = userId,
            Disputetype = request.DisputeType,
            Reason = request.Reason,
            Status = DisputeStatus.Pending,
            Evidence = request.Evidence != null ? JsonSerializer.Serialize(request.Evidence) : null,
            Createdat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow
        };

        _context.Disputes.Add(dispute);

        // Update classSession status
        classSession.Status = Disputed;

        await _context.SaveChangesAsync();

        _logger.LogInformation("{Role} {UserId} created dispute {DisputeId} for classSession {ClassSessionId}",
            role, userId, dispute.Disputeid, classSessionId);

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
                Message = $"Phụ huynh đã tạo tranh chấp cho buổi học #{classSessionId}. Lý do: {request.Reason}"
            });
            await _notificationService.CreateNotificationsAsync(notis);
        }

        // Notify the tutor too — otherwise they have no way to know a rebuttal is open for them.
        if (!string.IsNullOrEmpty(classSession.Tutorid))
        {
            await _notificationService.CreateNotificationAsync(new NotificationRequest
            {
                Userid = classSession.Tutorid,
                Title = "Có khiếu nại về buổi học của bạn",
                Message = $"Một khiếu nại đã được tạo cho buổi học #{classSessionId}. Bạn có thể xem chi tiết và gửi phản hồi."
            });
        }

        return new DisputeDetailResponse
        {
            DisputeId = dispute.Disputeid,
            BookingId = dispute.Bookingid,
            ClassSessionId = dispute.Classsessionid,
            DisputeType = request.DisputeType,
            Reason = dispute.Reason,
            Status = dispute.Status,
            Evidence = request.Evidence,
            CreatedAt = dispute.Createdat.HasValue ? dispute.Createdat.Value : (DateTime?)null,
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
            .Include(d => d.ClassSession)
                .ThenInclude(l => l!.Tutor)
                    .ThenInclude(t => t!.Tutor)
            .Select(d => new
            {
                d.Disputeid,
                d.Classsessionid,
                d.Bookingid,
                d.Disputetype,
                d.Status,
                d.Reason,
                TutorName = d.ClassSession!.Tutor!.Tutor!.Fullname,
                ClassSessionPrice = d.ClassSession.Lessonprice,
                d.Createdat
            })
            .ToListAsync();

        var disputes = rawDisputes.Select(d => new DisputeListResponse
        {
            DisputeId = d.Disputeid,
            ClassSessionId = d.Classsessionid,
            BookingId = d.Bookingid,
            DisputeType = d.Disputetype,
            Status = d.Status,
            Reason = d.Reason,
            TutorName = d.TutorName,
            ClassSessionPrice = d.ClassSessionPrice,
            CreatedAt = d.Createdat.HasValue ? d.Createdat.Value : (DateTime?)null
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
                : DateTime.SpecifyKind(startDate, DateTimeKind.Utc);
            var endUtc = endDate.Kind == DateTimeKind.Utc 
                ? endDate 
                : DateTime.SpecifyKind(endDate, DateTimeKind.Utc);

            var studentIds = role == UserRole.Parent
                ? await _context.Studentprofiles.Where(s => s.Parentid == userId).Select(s => s.Studentid).ToListAsync()
                : await _context.Studentprofiles.Where(s => s.Studentid == userId || s.Linkeduserid == userId).Select(s => s.Studentid).ToListAsync();

            if (studentIds == null || studentIds.Count == 0)
                return new List<CalendarDayResponse>();

            var classSessions = await _context.ClassSessions
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
                        StudentName = l.Booking?.Student?.Fullname,
                        TutorName = l.Booking?.Tutor?.Tutor?.Fullname,
                        SubjectName = l.Booking?.Subject?.Subjectname,
                        Status = l.Status,
                        MeetingLink = l.Meetinglink,
                        CheckOutTime = l.Checkouttime
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

    /// <summary>
    /// Upload dispute evidence for a classSession
    /// </summary>
    public async Task<string> UploadDisputeEvidenceAsync(int classSessionId, string userId, string role, IFormFile file)
    {
        var studentIds = role == UserRole.Parent
            ? await _context.Studentprofiles.Where(s => s.Parentid == userId).Select(s => s.Studentid).ToListAsync()
            : await _context.Studentprofiles.Where(s => s.Studentid == userId || s.Linkeduserid == userId).Select(s => s.Studentid).ToListAsync();

        var classSession = await _context.ClassSessions
            .FirstOrDefaultAsync(l => l.Classsessionid == classSessionId && studentIds.Contains(l.Studentid!))
            ?? throw new ClassSessionException(ClassSessionErrorCodes.ClassSessionNotFound, "Không tìm thấy buổi học hoặc bạn không có quyền truy cập", 404);

        if (classSession.Issettled == true)
            throw new ClassSessionException(ClassSessionErrorCodes.ClassSessionAlreadyConfirmed, "Buổi học đã thanh toán, không thể tạo tranh chấp", 400);

        // If a dispute already exists for this classSession (e.g. no-show just reported), evidence
        // attaches to it directly and is only allowed while still Pending — once investigating starts,
        // the formal record is locked (use the dispute chat channel to share more with admin instead).
        // When called BEFORE dispute creation (CreateDisputeForm collects evidence URLs first, then
        // submits them in CreateDisputeRequest.evidence), there's no dispute yet — nothing to check.
        var activeDispute = await _context.Disputes
            .Where(d => d.Classsessionid == classSessionId && d.Status != DisputeStatus.Resolved && d.Status != DisputeStatus.Closed)
            .FirstOrDefaultAsync();
        if (activeDispute != null && activeDispute.Status != DisputeStatus.Pending)
            throw new ClassSessionException(ClassSessionErrorCodes.InvalidClassSessionStatus,
                "Tranh chấp đã bước vào giai đoạn điều tra hoặc đã được giải quyết, không thể nộp thêm bằng chứng vào hồ sơ.", 400);

        // Ensure bucket exists
        await _storageService.EnsureBucketExistsAsync(StorageBucket.ClassSessionAttachments);

        // Upload to Storage using class-session-specific folder
        var folderPath = $"dispute-evidence-{classSessionId}";
        var fileUrl = await _storageService.UploadFileAsync(StorageBucket.ClassSessionAttachments, folderPath, file);

        if (activeDispute != null)
        {
            _context.DisputeEvidences.Add(new DisputeEvidence
            {
                Disputeid = activeDispute.Disputeid,
                Uploadedby = userId,
                Fileurl = fileUrl,
                Filetype = file.ContentType,
                Createdat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow
            });
            await _context.SaveChangesAsync();
        }

        return fileUrl;
    }
}
