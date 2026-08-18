using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.Helpers;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Entities;
using MV.DomainLayer.Exceptions;
using MV.DomainLayer.Helpers;
using MV.ApplicationLayer.Interfaces;
using System.Text.Json;
using static MV.DomainLayer.Constants.ClassSessionStatus;
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
    private readonly IBackgroundJobClient _backgroundJobClient;
    private readonly IClassSessionRescheduleProposalService _rescheduleProposalService;
    private readonly IClassSessionService _classSessionService;
    private readonly ILogger<ParentService> _logger;

    public ParentService(
        IAppDbContext context,
        ISettlementService settlementService,
        INotificationService notificationService,
        IFileStorageService storageService,
        IBackgroundJobClient backgroundJobClient,
        IClassSessionRescheduleProposalService rescheduleProposalService,
        IClassSessionService classSessionService,
        ILogger<ParentService> logger)
    {
        _context = context;
        _settlementService = settlementService;
        _notificationService = notificationService;
        _storageService = storageService;
        _backgroundJobClient = backgroundJobClient;
        _rescheduleProposalService = rescheduleProposalService;
        _classSessionService = classSessionService;
        _logger = logger;
    }

    /// <summary>
    /// Get classSessions pending parent confirmation
    /// </summary>
    public async Task<List<PendingClassSessionResponse>> GetPendingClassSessionsAsync(string userId, string role)
    {
        var studentIds = role == UserRole.Parent
            ? await _context.Studentprofiles.Where(s => s.Parentid == userId && s.Deletedat == null).Select(s => s.Studentid).ToListAsync()
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
            StudentId = l.Studentid,
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

        var rescheduleProposals = await _rescheduleProposalService.GetProposalHistoryAsync(classSessionId);

        // Lịch sử dời lịch (nếu có) — mirror của DisputeService.GetDisputeDetailAsync.
        var scheduleChanges = await _context.ClassSessionScheduleChanges
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
        var scheduleChangeConfirmerNames = await _context.Users.AsNoTracking()
            .Where(x => scheduleChangeConfirmerIds.Contains(x.Userid))
            .ToDictionaryAsync(x => x.Userid, x => x.Fullname ?? x.Username ?? x.Email);

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
            BookingStatus = classSession.Booking?.Status,
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
                Attachments = ReportAttachmentSerializer.ToUrls(
                    ReportAttachmentSerializer.Deserialize(classSession.ClassSessionReport.Attachments)),
                AttachmentDetails = ReportAttachmentSerializer.Deserialize(classSession.ClassSessionReport.Attachments),
                CreatedAt = classSession.ClassSessionReport.Createdat.HasValue ? classSession.ClassSessionReport.Createdat.Value : (DateTime?)null
            } : null,
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
            }).ToList(),
            RescheduleProposals = rescheduleProposals,
            PendingRescheduleProposal = rescheduleProposals
                .FirstOrDefault(x => x.Status == RescheduleProposalStatus.Pending)
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

        if (role == UserRole.Student)
        {
            var studentProfile = await _context.Studentprofiles
                .FirstOrDefaultAsync(s => s.Studentid == userId || s.Linkeduserid == userId);
            if (studentProfile?.Parentid != null)
                throw new ClassSessionException(
                    BookingErrorCodes.StudentManagedByParent,
                    "Tài khoản học sinh do phụ huynh quản lý không thể tự tạo tranh chấp",
                    403);
        }

        if (!DisputeTypes.All.Contains(request.DisputeType))
            throw new ArgumentException("Loại tranh chấp không hợp lệ");

        var snapshot = await _context.ClassSessions
            .AsNoTracking()
            .Where(l => l.Classsessionid == classSessionId && studentIds.Contains(l.Studentid!))
            .Select(l => new
            {
                l.Bookingid,
                l.Status,
                BookingStatus = l.Booking != null ? l.Booking.Status : null
            })
            .FirstOrDefaultAsync()
            ?? throw new ClassSessionException(
                ClassSessionErrorCodes.ClassSessionNotFound,
                "Không tìm thấy buổi học hoặc bạn không có quyền truy cập",
                404);

        if (!snapshot.Bookingid.HasValue)
            throw new ClassSessionException(ClassSessionErrorCodes.ClassSessionNotFound, "Buổi học không có booking hợp lệ", 400);
        if (DisputeSettlementPolicy.IsTerminalBooking(snapshot.BookingStatus))
            throw new ClassSessionException(ClassSessionErrorCodes.InvalidClassSessionStatus, "Booking đã kết thúc, không thể tạo tranh chấp mới", 400);
        if (!DisputeSettlementPolicy.IsEligibleClassSession(snapshot.Status))
            throw new ClassSessionException(ClassSessionErrorCodes.InvalidClassSessionStatus, "Chỉ buổi học đã diễn ra mới có thể tạo tranh chấp", 400);

        var uploadedEvidence = new List<string>();
        var newlyUploadedEvidence = new List<string>();
        var evidenceFolder = $"dispute-evidence-{classSessionId}";
        var disputeCommitted = false;

        try
        {
            if (request.Files?.Count > 0)
            {
                await _storageService.EnsureBucketExistsAsync(StorageBucket.ClassSessionAttachments);
                foreach (var file in request.Files.Where(f => f is { Length: > 0 }))
                {
                    var fileUrl = await _storageService.UploadFileAsync(
                        StorageBucket.ClassSessionAttachments,
                        evidenceFolder,
                        file);
                    newlyUploadedEvidence.Add(fileUrl);
                    uploadedEvidence.Add(fileUrl);
                }
            }

            if (request.Evidence?.Count > 0)
                uploadedEvidence.AddRange(request.Evidence.Where(url => !string.IsNullOrWhiteSpace(url)));

            Dispute dispute;
            ClassSession classSession;

            // Serialize with settlement/admin resolution using the shared lock order.
            await using (var tx = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable))
            {
                try
                {
                    var booking = await _context.Bookings
                        .FromSqlRaw(SqlQueries.LockBookingById, snapshot.Bookingid.Value)
                        .SingleOrDefaultAsync()
                        ?? throw new ClassSessionException(ClassSessionErrorCodes.ClassSessionNotFound, "Không tìm thấy booking của buổi học", 404);

                    classSession = await _context.ClassSessions
                        .FromSqlRaw(SqlQueries.LockClassSessionById, classSessionId)
                        .SingleOrDefaultAsync()
                        ?? throw new ClassSessionException(ClassSessionErrorCodes.ClassSessionNotFound, "Không tìm thấy buổi học", 404);

                    if (!studentIds.Contains(classSession.Studentid!))
                        throw new ClassSessionException(ClassSessionErrorCodes.ClassSessionNotFound, "Bạn không có quyền truy cập buổi học này", 404);
                    if (DisputeSettlementPolicy.IsTerminalBooking(booking.Status))
                        throw new ClassSessionException(ClassSessionErrorCodes.InvalidClassSessionStatus, "Booking đã kết thúc, không thể tạo tranh chấp mới", 400);
                    if (!DisputeSettlementPolicy.IsEligibleClassSession(classSession.Status))
                        throw new ClassSessionException(ClassSessionErrorCodes.InvalidClassSessionStatus, "Chỉ buổi học đã diễn ra mới có thể tạo tranh chấp", 400);
                    if (await _context.Disputes.AnyAsync(d => d.Classsessionid == classSessionId))
                        throw new ClassSessionException(ClassSessionErrorCodes.DisputeAlreadyExists, "Buổi học này đã có tranh chấp rồi", 400);

                    if (classSession.Issettled == true)
                    {
                        // Settlement already reduced this counter once. Reopen exactly one unit;
                        // no wallet balance changes until admin chooses Release/Refund.
                        classSession.Issettled = false;
                        booking.Sessionsremaining = DisputeSettlementPolicy.SessionsRemainingAfterOpeningDispute(
                            booking.Sessionsremaining,
                            wasSettled: true);
                        booking.Updatedat = TimeZoneHelper.UtcNow;
                    }

                    dispute = new Dispute
                    {
                        Classsessionid = classSessionId,
                        Bookingid = classSession.Bookingid,
                        Createdby = userId,
                        Disputetype = request.DisputeType,
                        Reason = request.Reason,
                        Status = DisputeStatus.Pending,
                        Evidence = uploadedEvidence.Count > 0
                            ? JsonSerializer.Serialize(uploadedEvidence.Distinct())
                            : null,
                        Createdat = TimeZoneHelper.UtcNow
                    };

                    _context.Disputes.Add(dispute);
                    classSession.Status = Disputed;
                    await _context.SaveChangesAsync();
                    await tx.CommitAsync();
                    disputeCommitted = true;
                }
                catch
                {
                    await tx.RollbackAsync();
                    throw;
                }
            }

            try
            {
                var jobId = _backgroundJobClient.Enqueue<IDisputeService>(
                    s => s.ClassifyDisputePriorityAsync(dispute.Disputeid, "system", true));
                _logger.LogInformation(
                    "Enqueued Hangfire job {JobId} to classify priority for dispute {DisputeId}",
                    jobId,
                    dispute.Disputeid);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to enqueue priority classification job for dispute {DisputeId}", dispute.Disputeid);
            }

            _logger.LogInformation(
                "{Role} {UserId} created dispute {DisputeId} for classSession {ClassSessionId}",
                role,
                userId,
                dispute.Disputeid,
                classSessionId);

            try
            {
                var admins = await _context.Users
                    .Where(u => u.Primaryrole == UserRole.Admin)
                    .Select(u => u.Userid)
                    .ToListAsync();
                if (admins.Count > 0)
                {
                    await _notificationService.CreateNotificationsAsync(admins.Select(adminId => new NotificationRequest
                    {
                        Userid = adminId,
                        Title = "Tranh chấp mới",
                        Message = $"Phụ huynh đã tạo tranh chấp cho buổi học #{classSessionId}. Lý do: {request.Reason}"
                    }));
                }

                if (!string.IsNullOrWhiteSpace(classSession.Tutorid))
                {
                    await _notificationService.CreateNotificationAsync(new NotificationRequest
                    {
                        Userid = classSession.Tutorid,
                        Title = "Có khiếu nại về buổi học của bạn",
                        Message = $"Một khiếu nại đã được tạo cho buổi học #{classSessionId}. Bạn có thể xem chi tiết và gửi phản hồi."
                    });
                }
            }
            catch (Exception notificationError)
            {
                _logger.LogWarning(
                    notificationError,
                    "Dispute {DisputeId} was created but one or more notifications failed",
                    dispute.Disputeid);
            }

            return new DisputeDetailResponse
            {
                DisputeId = dispute.Disputeid,
                BookingId = dispute.Bookingid,
                ClassSessionId = dispute.Classsessionid,
                DisputeType = request.DisputeType,
                Reason = dispute.Reason,
                Status = dispute.Status,
                Priority = dispute.Priority,
                PriorityReason = dispute.Priorityreason,
                Evidence = uploadedEvidence.Count > 0 ? uploadedEvidence.Distinct().ToList() : null,
                CreatedAt = dispute.Createdat,
                CreatedBy = new DisputeUserResponse { UserId = userId }
            };
        }
        catch
        {
            if (!disputeCommitted)
            {
                foreach (var fileUrl in newlyUploadedEvidence)
                {
                    try
                    {
                        await _storageService.DeleteFileAsync(
                            StorageBucket.ClassSessionAttachments,
                            evidenceFolder,
                            fileUrl);
                    }
                    catch (Exception cleanupError)
                    {
                        _logger.LogWarning(
                            cleanupError,
                            "Failed to clean orphan dispute evidence {FileUrl} for classSession {ClassSessionId}",
                            fileUrl,
                            classSessionId);
                    }
                }
            }

            throw;
        }
    }

    /// <summary>
    /// Get the signed-in parent/student's dispute history.
    /// </summary>
    public async Task<PagedList<DisputeListResponse>> GetParentDisputesAsync(
        string userId,
        PortalDisputeQueryRequest query)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 50);

        var disputesQuery = _context.Disputes
            .AsNoTracking()
            .Where(dispute => dispute.Createdby == userId)
            .ApplyPortalFilters(query);

        var totalCount = await disputesQuery.CountAsync();
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
        page = Math.Min(page, totalPages);

        var disputes = await disputesQuery
            .OrderForDisputeList(query.SortDirection)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(dispute => new DisputeListResponse
            {
                DisputeId = dispute.Disputeid,
                ClassSessionId = dispute.Classsessionid,
                BookingId = dispute.Bookingid,
                DisputeType = dispute.Disputetype,
                Status = dispute.Status,
                Reason = dispute.Reason,
                Priority = dispute.Priority,
                PriorityReason = dispute.Priorityreason,
                TutorName = dispute.ClassSession!.Tutor!.Tutor!.Fullname,
                ClassSessionPrice = dispute.ClassSession.Lessonprice,
                CreatedAt = dispute.Createdat
            })
            .ToListAsync();

        return new PagedList<DisputeListResponse>(disputes, totalCount, page, pageSize);
    }

    /// <summary>
    /// Get parent calendar view
    /// </summary>
    /// <summary>
    /// True nếu buổi này có đề xuất đổi lịch (tính năng chủ động chọn giờ mới) đang Pending và
    /// chưa hết hạn. Nhân bản có chủ đích từ helper cùng tên trong <c>ClassSessionService</c> —
    /// 2 class khác nhau, không chia sẻ được private static method.
    /// </summary>
    private static bool ResolveHasPendingReschedule(IEnumerable<ClassSessionRescheduleProposal> proposals)
    {
        var now = TimeZoneHelper.UtcNow;
        return proposals.Any(x => x.Status == RescheduleProposalStatus.Pending && x.Expiresat > now);
    }

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
                        StudentName = l.Booking?.Student?.Fullname,
                        TutorName = l.Booking?.Tutor?.Tutor?.Fullname,
                        SubjectName = l.Booking?.Subject?.Subjectname,
                        Status = l.Status,
                        BookingStatus = l.Booking?.Status,
                        MeetingLink = l.Meetinglink,
                        CheckOutTime = l.Checkouttime,
                        HasRecording = RecordingStatusResolver.Resolve(l.Recordingurl, l.Recordings3key, l.Recordingsid, l.Checkouttime.HasValue).Status == "available",
                        HasPendingReschedule = ResolveHasPendingReschedule(l.RescheduleProposals)
                    }).ToList()
                })
                .ToList();
        }
        catch (Exception ex)
        {
            throw new Exception($"Error getting parent calendar: {ex.Message}", ex);
        }
    }

    public async Task<List<ParentChildClassSessionResponse>> GetChildClassSessionsAsync(
        string userId, string role, string studentId, DateTime? startDate, DateTime? endDate)
    {
        var studentIds = role == UserRole.Parent
            ? await _context.Studentprofiles.Where(s => s.Parentid == userId).Select(s => s.Studentid).ToListAsync()
            : await _context.Studentprofiles.Where(s => s.Studentid == userId || s.Linkeduserid == userId).Select(s => s.Studentid).ToListAsync();

        // Chặn xem buổi học của con người khác.
        if (!studentIds.Contains(studentId))
            throw new UnauthorizedAccessException("Bạn không có quyền xem lịch học của học sinh này.");

        var query = _context.ClassSessions
            .AsNoTracking()
            .Where(l => l.Studentid == studentId);

        if (startDate.HasValue)
        {
            var startUtc = DateTime.SpecifyKind(startDate.Value, DateTimeKind.Utc);
            query = query.Where(l => l.Scheduledstart >= startUtc);
        }

        if (endDate.HasValue)
        {
            var endUtc = DateTime.SpecifyKind(endDate.Value, DateTimeKind.Utc);
            query = query.Where(l => l.Scheduledstart <= endUtc);
        }

        var classSessions = await query
            .Include(l => l.Booking)
                .ThenInclude(b => b!.Tutorsubjectgradeprice)
                    .ThenInclude(p => p!.Subject)
            .Include(l => l.Booking)
                .ThenInclude(b => b!.Student)
            .Include(l => l.Tutor)
                .ThenInclude(t => t!.Tutor)
            .Include(l => l.RescheduleProposals)
            .OrderBy(l => l.Scheduledstart)
            .ToListAsync();

        return classSessions.Select(l => new ParentChildClassSessionResponse
        {
            ClassSessionId = l.Classsessionid,
            BookingId = l.Bookingid,
            ScheduledStart = l.Scheduledstart,
            ScheduledEnd = l.Scheduledend,
            StudentId = l.Studentid,
            StudentName = l.Booking?.Student?.Fullname,
            TutorId = l.Tutorid,
            TutorName = l.Tutor?.Tutor?.Fullname,
            TutorAvatarUrl = l.Tutor?.Tutor?.Avatarurl,
            SubjectName = l.Booking?.Subject?.Subjectname,
            Status = l.Status,
            BookingStatus = l.Booking?.Status,
            MeetingLink = l.Meetinglink,
            CheckOutTime = l.Checkouttime,
            HasRecording = RecordingStatusResolver.Resolve(l.Recordingurl, l.Recordings3key, l.Recordingsid, l.Checkouttime.HasValue).Status == "available",
            HasPendingReschedule = ResolveHasPendingReschedule(l.RescheduleProposals)
        }).ToList();
    }

    public async Task<ParentChildClassSessionResponse?> GetNextClassSessionAsync(
        string userId, string role, string? studentId)
    {
        var studentIds = role == UserRole.Parent
            ? await _context.Studentprofiles.Where(s => s.Parentid == userId).Select(s => s.Studentid).ToListAsync()
            : await _context.Studentprofiles.Where(s => s.Studentid == userId || s.Linkeduserid == userId).Select(s => s.Studentid).ToListAsync();

        if (studentId != null)
        {
            if (!studentIds.Contains(studentId))
                throw new UnauthorizedAccessException("Bạn không có quyền xem lịch học của học sinh này.");
            studentIds = new List<string> { studentId };
        }

        if (studentIds.Count == 0)
            return null;

        var now = TimeZoneHelper.UtcNow;

        // Buổi in_progress lấy cả khi quá giờ (gia sư chưa check-out); scheduled chỉ lấy khi
        // chưa kết thúc. Loại `reserved` (chờ gia sư nhận) và booking huỷ/hết hạn thanh toán.
        var session = await _context.ClassSessions
            .AsNoTracking()
            .Where(l => l.Studentid != null && studentIds.Contains(l.Studentid)
                     && l.Checkouttime == null
                     && ((l.Status == Scheduled && l.Scheduledend >= now) || l.Status == InProgress)
                     && l.Booking != null
                     && l.Booking.Status != BookingStatus.Cancelled
                     && l.Booking.Status != BookingStatus.CancelledNoshow
                     && l.Booking.Status != BookingStatus.PaymentTimeout)
            .Include(l => l.Booking)
                .ThenInclude(b => b!.Tutorsubjectgradeprice)
                    .ThenInclude(p => p!.Subject)
            .Include(l => l.Booking)
                .ThenInclude(b => b!.Student)
            .Include(l => l.Tutor)
                .ThenInclude(t => t!.Tutor)
            .Include(l => l.RescheduleProposals)
            .OrderByDescending(l => l.Status == InProgress)
            .ThenBy(l => l.Scheduledstart)
            .FirstOrDefaultAsync();

        if (session == null)
            return null;

        // Buổi bị khoá vì chưa thanh toán đợt 2 thì chưa học được.
        if (await _classSessionService.IsSessionBlockedByRemainingPaymentAsync(session.Classsessionid))
            return null;

        return new ParentChildClassSessionResponse
        {
            ClassSessionId = session.Classsessionid,
            BookingId = session.Bookingid,
            ScheduledStart = session.Scheduledstart,
            ScheduledEnd = session.Scheduledend,
            StudentId = session.Studentid,
            StudentName = session.Booking?.Student?.Fullname,
            TutorId = session.Tutorid,
            TutorName = session.Tutor?.Tutor?.Fullname,
            TutorAvatarUrl = session.Tutor?.Tutor?.Avatarurl,
            SubjectName = session.Booking?.Subject?.Subjectname,
            Status = session.Status,
            BookingStatus = session.Booking?.Status,
            MeetingLink = session.Meetinglink,
            CheckOutTime = session.Checkouttime,
            HasRecording = RecordingStatusResolver.Resolve(session.Recordingurl, session.Recordings3key, session.Recordingsid, session.Checkouttime.HasValue).Status == "available",
            HasPendingReschedule = ResolveHasPendingReschedule(session.RescheduleProposals)
        };
    }

    public async Task<ParentHomeStatsResponse> GetHomeStatsAsync(string userId, string role, string? studentId)
    {
        var allStudents = role == UserRole.Parent
            ? await _context.Studentprofiles.Where(s => s.Parentid == userId && s.Deletedat == null).Select(s => s.Studentid).ToListAsync()
            : await _context.Studentprofiles.Where(s => s.Studentid == userId || s.Linkeduserid == userId).Select(s => s.Studentid).ToListAsync();

        if (allStudents.Count == 0)
            return new ParentHomeStatsResponse();

        // Buổi/chờ xác nhận tính theo con đang xem; số con thì luôn tính trên mọi con.
        if (studentId != null && !allStudents.Contains(studentId))
            throw new UnauthorizedAccessException("Bạn không có quyền xem số liệu của học sinh này.");

        var students = studentId != null ? new List<string> { studentId } : allStudents;

        var now = TimeZoneHelper.UtcNow;
        var weekStart = now.Date.AddDays(-(((int)now.DayOfWeek + 6) % 7));
        var weekEnd = weekStart.AddDays(7);

        // Giữ `reserved`: phụ huynh đã trả cọc nên buổi giữ chỗ vẫn đếm là buổi
        // trong tuần — khớp với danh sách buổi hiện trên Home.
        var sessionsThisWeek = await _context.ClassSessions
            .CountAsync(l => l.Studentid != null && students.Contains(l.Studentid)
                          && l.Scheduledstart >= weekStart && l.Scheduledstart < weekEnd
                          && l.Status != Cancelled && l.Status != CancelledNoshow);

        // "Đang học" = có booking đã trả phí và chưa kết thúc; `pending_tutor`/`accepted`
        // (chờ gia sư nhận, chưa trả phí) và mọi booking đã đóng đều không tính.
        var learningStatuses = new[]
        {
            BookingStatus.DepositPaid,
            BookingStatus.Paid,
            BookingStatus.Ongoing,
            BookingStatus.PendingRemainingPayment,
        };

        var childrenLearning = await _context.Bookings
            .Where(b => b.Studentid != null && allStudents.Contains(b.Studentid)
                     && learningStatuses.Contains(b.Status))
            .Select(b => b.Studentid)
            .Distinct()
            .CountAsync();

        var pendingConfirmation = await _context.ClassSessions
            .CountAsync(l => l.Studentid != null && students.Contains(l.Studentid)
                          && l.Status == PendingConfirmation && l.Issettled != true);

        return new ParentHomeStatsResponse
        {
            SessionsThisWeek = sessionsThisWeek,
            ChildrenLearning = childrenLearning,
            ChildrenTotal = allStudents.Count,
            PendingConfirmation = pendingConfirmation,
        };
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
