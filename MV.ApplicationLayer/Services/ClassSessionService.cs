using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.Helpers;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Entities;
using MV.DomainLayer.Exceptions;
using MV.DomainLayer.Helpers;
using MV.ApplicationLayer.Interfaces;
using MV.ApplicationLayer.RepositoryInterfaces;
using static MV.DomainLayer.Constants.ClassSessionStatus;
using static MV.DomainLayer.Constants.PaymentStatus;

namespace MV.ApplicationLayer.Services;

public partial class ClassSessionService : IClassSessionService
{
    private readonly IClassSessionRepository _classSessionRepo;
    private readonly IBookingRepository _bookingRepo;
    private readonly IStudentRepository _studentRepo;
    private readonly IChatService _chatService;
    private readonly INotificationService _notificationService;
    private readonly IZaloOAService _zaloOAService;
    private readonly IFileStorageService _storageService;
    private readonly ILogger<ClassSessionService> _logger;

    // Retained for transaction management only (BeginTransactionAsync)
    private readonly IAppDbContext _context;

    private const string ClassSessionAttachmentBucket = StorageBucket.ClassSessionAttachments;

    public ClassSessionService(
        IClassSessionRepository classSessionRepo,
        IBookingRepository bookingRepo,
        IStudentRepository studentRepo,
        IAppDbContext context,
        IChatService chatService,
        INotificationService notificationService,
        IZaloOAService zaloOAService,
        IFileStorageService storageService,
        ILogger<ClassSessionService> logger)
    {
        _classSessionRepo = classSessionRepo;
        _bookingRepo = bookingRepo;
        _studentRepo = studentRepo;
        _context = context;
        _chatService = chatService;
        _notificationService = notificationService;
        _zaloOAService = zaloOAService;
        _storageService = storageService;
        _logger = logger;
    }


    public async Task AutoCreateClassSessionsAsync(int bookingId, CancellationToken ct = default)
    {
        var booking = await _bookingRepo.FindWithRelationsAsync(bookingId)
            ?? throw new BookingException(BookingErrorCodes.BookingNotFound, "Không tìm thấy booking", 404);

        if (booking.Status != BookingStatus.Paid && booking.Status != BookingStatus.DepositPaid
            && booking.Paymentstatus != Escrowed && booking.Paymentstatus != DepositEscrowed)
        {
            _logger.LogWarning("Booking {Id} not paid/deposited yet", bookingId);
            return;
        }

        await using var tx = await _context.Database.BeginTransactionAsync(ct);
        try
        {
            var classSessions = await _context.ClassSessions
                .Where(l => l.Bookingid == bookingId)
                .OrderBy(l => l.Scheduledstart)
                .ToListAsync(ct);

            if (classSessions.Count == 0)
                throw new BookingException(BookingErrorCodes.InvalidSchedule, "Không tìm thấy lịch học đã đặt", 400);

            foreach (var classSession in classSessions.Where(l => l.Status == Reserved))
            {
                classSession.Status = Scheduled;
            }

            await _context.SaveChangesAsync(ct);

            if (booking.Tutorid != null && booking.Studentid != null)
            {
                // Tencent RTC: RoomId = classSessionId (deterministic, không cần tạo link bên ngoài)
                foreach (var classSession in classSessions.Where(l => string.IsNullOrWhiteSpace(l.Meetinglink)))
                {
                    classSession.Meetinglink = classSession.Classsessionid.ToString();
                }
                await _context.SaveChangesAsync(ct);
                _logger.LogInformation("Assigned Tencent RTC RoomId for {Count} classSessions in booking {BookingId}",
                    classSessions.Count, bookingId);
            }

            await tx.CommitAsync(ct);

            await _chatService.SendMeetLinksAsync(bookingId, classSessions.Select((l, i) => new ClassSessionMiniResponse
            {
                ClassSessionId = l.Classsessionid,
                BookingId = bookingId,
                SessionIndex = i + 1,
                ScheduledStart = l.Scheduledstart,
                ScheduledEnd = l.Scheduledend,
                MeetingLink = l.Meetinglink ?? "",
                Status = l.Status ?? Scheduled
            }).ToList());

            _logger.LogInformation("Activated {Count} reserved classSessions for booking {BookingId}", classSessions.Count, bookingId);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<PagedList<ClassSessionResponse>> GetTutorClassSessionsAsync(string tutorId, int page, int pageSize, DateTime? fromDate, string? status)
    {
        var (items, totalCount) = await _classSessionRepo.GetTutorClassSessionsPagedAsync(tutorId, page, pageSize, fromDate, status);
        var dtos = items.Select(MapToClassSessionResponse).ToList();
        return new PagedList<ClassSessionResponse>(dtos, totalCount, page, pageSize);
    }

    public async Task<PagedList<ClassSessionResponse>> GetParentClassSessionsAsync(string parentId, int page, int pageSize, DateTime? fromDate, string? status)
    {
        var studentIds = await _studentRepo.GetStudentIdsByParentIdAsync(parentId);
        var (items, totalCount) = await _classSessionRepo.GetByStudentIdsPagedAsync(studentIds, page, pageSize, fromDate, status);
        var dtos = items.Select(MapToClassSessionResponse).ToList();
        return new PagedList<ClassSessionResponse>(dtos, totalCount, page, pageSize);
    }

    public async Task<ClassSessionResponse?> GetClassSessionByIdAsync(int classSessionId, string userId, bool isParent)
    {
        var classSession = await _classSessionRepo.GetByIdWithDetailsAsync(classSessionId);
        if (classSession == null) return null;

        if (isParent)
        {
            var studentIds = await _studentRepo.GetStudentIdsByParentIdAsync(userId);
            if (classSession.Studentid == null || !studentIds.Contains(classSession.Studentid))
                return null;
        }
        else
        {
            if (classSession.Tutorid != userId && classSession.Studentid != userId)
                return null;
        }

        return MapToClassSessionResponse(classSession);
    }

    private async Task<List<ClassSessionDateSlot>> CalculateClassSessionDatesAsync(List<ScheduleItemRequest> schedule, int sessionCount, DateTime desiredStartUtc, string? tutorId)
    {
        var result = new List<ClassSessionDateSlot>();

        // Chuẩn hóa desiredStartUtc về UTC
        var desiredStartNorm = desiredStartUtc.Kind == DateTimeKind.Utc
            ? desiredStartUtc
            : DateTime.SpecifyKind(desiredStartUtc, DateTimeKind.Utc);

        // FE sends UTC — compare using UTC dates directly
        var desiredDateUtc = desiredStartNorm.Date;
        var todayUtc = TimeZoneHelper.UtcNow.Date;

        var effectiveDateUtc = desiredDateUtc >= todayUtc ? desiredDateUtc : todayUtc;
        if (desiredDateUtc < todayUtc)
            _logger.LogWarning("Booking desired start {DesiredDate} is in the past (today UTC: {Today}). Scheduling from today.", desiredDateUtc, todayUtc);

        // effectiveStartUtc = 00:00 UTC of effective date
        var effectiveStartUtc = new DateTime(effectiveDateUtc.Year, effectiveDateUtc.Month, effectiveDateUtc.Day, 0, 0, 0, DateTimeKind.Utc);
        var effectiveStartVn = effectiveStartUtc;

        var calendar = new Dictionary<int, List<ScheduleItemRequest>>();
        // Initialize calendar with keys 1-7 (Monday-Sunday)
        for (int i = 1; i <= 7; i++)
            calendar[i] = [];

        foreach (var item in schedule)
        {
            if (calendar.ContainsKey(item.DayOfWeek))
                calendar[item.DayOfWeek].Add(item);
            else
                calendar[item.DayOfWeek] = [item];
        }

        // currentDate là ngày theo giờ Việt Nam
        var currentDate = effectiveStartVn.Date;

        while (result.Count < sessionCount)
        {
            // Convert C# DayOfWeek (0=Sunday, 1=Monday...) to ISO format (1=Monday, 7=Sunday)
            var dayOfWeek = currentDate.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)currentDate.DayOfWeek;
            var timeSlots = calendar[dayOfWeek];

            if (timeSlots.Count == 0)
            {
                currentDate = currentDate.AddDays(1);
                continue;
            }

            foreach (var slot in timeSlots)
            {
                if (result.Count >= sessionCount) break;

                // ParseTimeSlot nhận ngày VN, trả về UTC
                var (startUtc, endUtc) = ParseTimeSlot(slot.StartTime, slot.EndTime, currentDate);

                // Bỏ qua slot đã QUA (dùng < để slot đúng giờ bắt đầu vẫn được tính)
                if (startUtc < effectiveStartUtc)
                    continue;

                var hasConflict = await _classSessionRepo.HasConflictAsync(tutorId!, startUtc, endUtc);
                if (!hasConflict)
                {
                    result.Add(new ClassSessionDateSlot { StartTime = startUtc, EndTime = endUtc });
                }
            }

            currentDate = currentDate.AddDays(1);
        }

        return result;
    }

    /// <summary>
    /// Parses HH:mm UTC time strings with a UTC date into UTC DateTimes.
    /// </summary>
    private static (DateTime utcStart, DateTime utcEnd) ParseTimeSlot(string startTime, string endTime, DateTime utcDate)
    {
        var startParts = startTime.Split(':');
        var endParts = endTime.Split(':');

        // FE sends UTC — build UTC DateTimes directly
        var utcStart = new DateTime(utcDate.Year, utcDate.Month, utcDate.Day,
            int.Parse(startParts[0]), int.Parse(startParts[1]), 0, DateTimeKind.Utc);
        var utcEnd = new DateTime(utcDate.Year, utcDate.Month, utcDate.Day,
            int.Parse(endParts[0]), int.Parse(endParts[1]), 0, DateTimeKind.Utc);

        return (utcStart, utcEnd);
    }

    private static ClassSessionResponse MapToClassSessionResponse(ClassSession classSession)
    {
        var booking = classSession.Booking;
        var student = booking?.Student;
        var subject = booking?.Tutorsubjectgradeprice?.Subject;
        return new ClassSessionResponse
        {
            ClassSessionId = classSession.Classsessionid,
            BookingId = classSession.Bookingid ?? 0,
            TutorId = classSession.Tutorid,
            StudentId = classSession.Studentid,
            // Trả về giờ Việt Nam để frontend hiển thị đúng
            ScheduledStart = classSession.Scheduledstart,
            ScheduledEnd = classSession.Scheduledend,
            MeetingLink = classSession.Meetinglink,
            ClassSessionPrice = classSession.Lessonprice,
            Status = classSession.Status ?? "",
            CheckInTime = classSession.Checkintime,
            CheckOutTime = classSession.Checkouttime,
            IsTutorPresent = classSession.Istutorpresent,
            IsStudentPresent = classSession.Isstudentpresent,
            CreatedAt = classSession.Createdat.HasValue ? classSession.Createdat.Value : (DateTime?)null,
            Student = student != null ? new StudentMiniResponse
            {
                StudentId = student.Studentid,
                FullName = student.Fullname,
                GradeLevelId = student.Gradelevelid,
                GradeLevel = student.Gradelevel,
                GradeLevelName = student.Gradelevel
            } : null,
            Subject = subject != null ? new SubjectResponse
            {
                SubjectId = subject.Subjectid,
                SubjectName = subject.Subjectname
            } : null,
            Tutor = classSession.Tutor?.Tutor != null ? new TutorMiniResponse
            {
                TutorId = classSession.Tutor.Tutorid,
                FullName = classSession.Tutor.Tutor.Fullname,
                AvatarUrl = classSession.Tutor.Tutor.Avatarurl,
                HourlyRate = classSession.Booking?.Priceperhour
            } : null
        };
    }

    /// <summary>
    /// Chuyển DateTime (UTC hoặc Unspecified-treated-as-UTC từ EF Core) sang giờ Việt Nam.
    /// MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow provided by VietnamTimeHelper via using static
    /// </summary>

    private record ClassSessionDateSlot
    {
        public DateTime StartTime { get; init; }
        public DateTime EndTime { get; init; }
    }

    public async Task RefreshMeetLinksForTutorAsync(string tutorId)
    {
        try
        {
            // Tencent RTC: assign RoomId = classSessionId cho tất cả classSession online/hybrid sắp tới chưa có RoomId
            var classSessions = await _context.ClassSessions
                .Include(l => l.Booking)
                .Where(l => l.Tutorid == tutorId
                    && (l.Meetinglink == null || l.Meetinglink == "")
                    && l.Scheduledstart > TimeZoneHelper.UtcNow
                    && l.Status != Cancelled
                    && l.Status != CancelledNoshow
                    && l.Status != Completed
                    && l.Status != NoShow)
                .ToListAsync();

            var onlineClassSessions = classSessions
                .ToList();

            if (onlineClassSessions.Count == 0) return;

            foreach (var classSession in onlineClassSessions)
            {
                classSession.Meetinglink = classSession.Classsessionid.ToString();
            }

            await _context.SaveChangesAsync();
            _logger.LogInformation("Assigned Tencent RTC RoomId for {Count} classSessions for tutor {TutorId}", onlineClassSessions.Count, tutorId);

            // Gửi chat message cho từng booking (gom nhóm theo bookingId)
            var byBooking = onlineClassSessions
                .GroupBy(l => l.Bookingid ?? 0)
                .Where(g => g.Key > 0);

            foreach (var group in byBooking)
            {
                try
                {
                    var sorted = group.OrderBy(l => l.Scheduledstart).ToList();
                    await _chatService.SendMeetLinksAsync(group.Key, sorted.Select((l, i) => new ClassSessionMiniResponse
                    {
                        ClassSessionId      = l.Classsessionid,
                        BookingId     = group.Key,
                        SessionIndex  = i + 1,
                        ScheduledStart = l.Scheduledstart,
                        ScheduledEnd   = l.Scheduledend,
                        MeetingLink   = l.Meetinglink!,
                        Status        = l.Status ?? ""
                    }).ToList());
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to send Tencent RTC room info chat for booking {BookingId}", group.Key);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RefreshMeetLinksForTutorAsync failed for tutor {TutorId}", tutorId);
        }
    }

    public async Task<ClassSessionResponse> CancelClassSessionAsync(int classSessionId, string userId, string userRole, string? reason = null)
    {
        var classSession = await _classSessionRepo.GetByIdWithDetailsAsync(classSessionId)
            ?? throw new KeyNotFoundException("Không tìm thấy buổi học.");

        // Ownership check
        if (userRole == UserRole.Tutor)
        {
            if (classSession.Tutorid != userId)
                throw new UnauthorizedAccessException("Bạn không có quyền hủy buổi học này.");
        }
        else if (userRole == UserRole.Parent)
        {
            var studentIds = await _studentRepo.GetStudentIdsByParentIdAsync(userId);
            if (classSession.Studentid == null || !studentIds.Contains(classSession.Studentid))
                throw new UnauthorizedAccessException("Bạn không có quyền hủy buổi học này.");
        }
        else
        {
            // Student
            var linkedUserId = classSession.Booking?.Student?.Linkeduserid;
            if (classSession.Studentid != userId && linkedUserId != userId)
                throw new UnauthorizedAccessException("Bạn không có quyền hủy buổi học này.");
        }

        // Single-classSession cancel is blocked: escrow release requires full booking cancel.
        throw new InvalidOperationException(
            "Không hỗ trợ hủy từng buổi học. Vui lòng hủy toàn bộ booking để nhận hoàn tiền tương ứng.");
    }

    public async Task<(IReadOnlyList<StudentClassSessionSummaryResponse> Items, int TotalCount)> GetStudentClassSessionsAsync(
        string studentProfileId, int page, int pageSize, string? status)
    {
        var (items, total) = await _classSessionRepo.GetStudentClassSessionsPagedAsync(studentProfileId, page, pageSize, status);
        return (items, total);
    }

    public Task<StudentClassSessionDetailResponse?> GetStudentClassSessionDetailAsync(int classSessionId, string studentProfileId)
        => _classSessionRepo.GetStudentClassSessionDetailAsync(classSessionId, studentProfileId);

    public Task<IReadOnlyList<StudentClassSessionSummaryResponse>> GetStudentPendingClassSessionsAsync(string studentProfileId)
        => _classSessionRepo.GetStudentPendingClassSessionsAsync(studentProfileId);
}
