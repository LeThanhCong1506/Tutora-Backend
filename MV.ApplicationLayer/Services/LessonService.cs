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
using static MV.DomainLayer.Constants.LessonStatus;
using static MV.DomainLayer.Constants.PaymentStatus;

namespace MV.ApplicationLayer.Services;

public partial class LessonService : ILessonService
{
    private readonly ILessonRepository _lessonRepo;
    private readonly IBookingRepository _bookingRepo;
    private readonly IStudentRepository _studentRepo;
    private readonly IChatService _chatService;
    private readonly INotificationService _notificationService;
    private readonly IZaloOAService _zaloOAService;
    private readonly IFileStorageService _storageService;
    private readonly ILogger<LessonService> _logger;

    // Retained for transaction management only (BeginTransactionAsync)
    private readonly IAppDbContext _context;

    private const string LessonAttachmentBucket = StorageBucket.LessonAttachments;

    public LessonService(
        ILessonRepository lessonRepo,
        IBookingRepository bookingRepo,
        IStudentRepository studentRepo,
        IAppDbContext context,
        IChatService chatService,
        INotificationService notificationService,
        IZaloOAService zaloOAService,
        IFileStorageService storageService,
        ILogger<LessonService> logger)
    {
        _lessonRepo = lessonRepo;
        _bookingRepo = bookingRepo;
        _studentRepo = studentRepo;
        _context = context;
        _chatService = chatService;
        _notificationService = notificationService;
        _zaloOAService = zaloOAService;
        _storageService = storageService;
        _logger = logger;
    }


    public async Task AutoCreateLessonsAsync(int bookingId, CancellationToken ct = default)
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
            var lessons = await _context.Lessons
                .Where(l => l.Bookingid == bookingId)
                .OrderBy(l => l.Scheduledstart)
                .ToListAsync(ct);

            if (lessons.Count == 0)
                throw new BookingException(BookingErrorCodes.InvalidSchedule, "Không tìm thấy lịch học đã đặt", 400);

            foreach (var lesson in lessons.Where(l => l.Status == Reserved))
            {
                lesson.Status = Scheduled;
            }

            await _context.SaveChangesAsync(ct);

            if (booking.Tutorid != null && booking.Studentid != null)
            {
                // Video call: RoomId = lessonId (deterministic, không cần tạo link bên ngoài)
                foreach (var lesson in lessons.Where(l => string.IsNullOrWhiteSpace(l.Meetinglink)))
                {
                    lesson.Meetinglink = lesson.Lessonid.ToString();
                }
                await _context.SaveChangesAsync(ct);
                _logger.LogInformation("Assigned video call RoomId for {Count} lessons in booking {BookingId}",
                    lessons.Count, bookingId);
            }

            await tx.CommitAsync(ct);

            await _chatService.SendMeetLinksAsync(bookingId, lessons.Select((l, i) => new LessonMiniResponse
            {
                LessonId = l.Lessonid,
                BookingId = bookingId,
                SessionIndex = i + 1,
                ScheduledStart = l.Scheduledstart,
                ScheduledEnd = l.Scheduledend,
                MeetingLink = l.Meetinglink ?? "",
                Status = l.Status ?? Scheduled
            }).ToList());

            _logger.LogInformation("Activated {Count} reserved lessons for booking {BookingId}", lessons.Count, bookingId);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<PagedList<LessonResponse>> GetTutorLessonsAsync(string tutorId, int page, int pageSize, DateTime? fromDate, string? status)
    {
        var (items, totalCount) = await _lessonRepo.GetTutorLessonsPagedAsync(tutorId, page, pageSize, fromDate, status);
        var dtos = items.Select(MapToLessonResponse).ToList();
        return new PagedList<LessonResponse>(dtos, totalCount, page, pageSize);
    }

    public async Task<PagedList<LessonResponse>> GetParentLessonsAsync(string parentId, int page, int pageSize, DateTime? fromDate, string? status)
    {
        var studentIds = await _studentRepo.GetStudentIdsByParentIdAsync(parentId);
        var (items, totalCount) = await _lessonRepo.GetByStudentIdsPagedAsync(studentIds, page, pageSize, fromDate, status);
        var dtos = items.Select(MapToLessonResponse).ToList();
        return new PagedList<LessonResponse>(dtos, totalCount, page, pageSize);
    }

    public async Task<LessonResponse?> GetLessonByIdAsync(int lessonId, string userId, bool isParent)
    {
        var lesson = await _lessonRepo.GetByIdWithDetailsAsync(lessonId);
        if (lesson == null) return null;

        if (isParent)
        {
            var studentIds = await _studentRepo.GetStudentIdsByParentIdAsync(userId);
            if (lesson.Studentid == null || !studentIds.Contains(lesson.Studentid))
                return null;
        }
        else
        {
            if (lesson.Tutorid != userId && lesson.Studentid != userId)
                return null;
        }

        return MapToLessonResponse(lesson);
    }

    private async Task<List<LessonDateSlot>> CalculateLessonDatesAsync(List<ScheduleItemRequest> schedule, int sessionCount, DateTime desiredStartUtc, string? tutorId)
    {
        var result = new List<LessonDateSlot>();

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

                var hasConflict = await _lessonRepo.HasConflictAsync(tutorId!, startUtc, endUtc);
                if (!hasConflict)
                {
                    result.Add(new LessonDateSlot { StartTime = startUtc, EndTime = endUtc });
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

    private static LessonResponse MapToLessonResponse(Lesson lesson)
    {
        var booking = lesson.Booking;
        var student = booking?.Student;
        var subject = booking?.Tutorsubjectgradeprice?.Subject;
        return new LessonResponse
        {
            LessonId = lesson.Lessonid,
            BookingId = lesson.Bookingid ?? 0,
            TutorId = lesson.Tutorid,
            StudentId = lesson.Studentid,
            // Trả về giờ Việt Nam để frontend hiển thị đúng
            ScheduledStart = lesson.Scheduledstart,
            ScheduledEnd = lesson.Scheduledend,
            MeetingLink = lesson.Meetinglink,
            LessonPrice = lesson.Lessonprice,
            Status = lesson.Status ?? "",
            CheckInTime = lesson.Checkintime,
            CheckOutTime = lesson.Checkouttime,
            IsTutorPresent = lesson.Istutorpresent,
            IsStudentPresent = lesson.Isstudentpresent,
            CreatedAt = lesson.Createdat.HasValue ? lesson.Createdat.Value : (DateTime?)null,
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
            Tutor = lesson.Tutor?.Tutor != null ? new TutorMiniResponse
            {
                TutorId = lesson.Tutor.Tutorid,
                FullName = lesson.Tutor.Tutor.Fullname,
                AvatarUrl = lesson.Tutor.Tutor.Avatarurl,
                HourlyRate = lesson.Booking?.Priceperhour
            } : null
        };
    }

    /// <summary>
    /// Chuyển DateTime (UTC hoặc Unspecified-treated-as-UTC từ EF Core) sang giờ Việt Nam.
    /// MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow provided by VietnamTimeHelper via using static
    /// </summary>

    private record LessonDateSlot
    {
        public DateTime StartTime { get; init; }
        public DateTime EndTime { get; init; }
    }

    public async Task RefreshMeetLinksForTutorAsync(string tutorId)
    {
        try
        {
            // Video call: assign RoomId = lessonId cho tất cả lesson online/hybrid sắp tới chưa có RoomId
            var lessons = await _context.Lessons
                .Include(l => l.Booking)
                .Where(l => l.Tutorid == tutorId
                    && (l.Meetinglink == null || l.Meetinglink == "")
                    && l.Scheduledstart > TimeZoneHelper.UtcNow
                    && l.Status != Cancelled
                    && l.Status != CancelledNoshow
                    && l.Status != Completed
                    && l.Status != NoShow)
                .ToListAsync();

            var onlineLessons = lessons
                .ToList();

            if (onlineLessons.Count == 0) return;

            foreach (var lesson in onlineLessons)
            {
                lesson.Meetinglink = lesson.Lessonid.ToString();
            }

            await _context.SaveChangesAsync();
            _logger.LogInformation("Assigned video call RoomId for {Count} lessons for tutor {TutorId}", onlineLessons.Count, tutorId);

            // Gửi chat message cho từng booking (gom nhóm theo bookingId)
            var byBooking = onlineLessons
                .GroupBy(l => l.Bookingid ?? 0)
                .Where(g => g.Key > 0);

            foreach (var group in byBooking)
            {
                try
                {
                    var sorted = group.OrderBy(l => l.Scheduledstart).ToList();
                    await _chatService.SendMeetLinksAsync(group.Key, sorted.Select((l, i) => new LessonMiniResponse
                    {
                        LessonId      = l.Lessonid,
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

    public async Task<LessonResponse> CancelLessonAsync(int lessonId, string userId, string userRole, string? reason = null)
    {
        var lesson = await _lessonRepo.GetByIdWithDetailsAsync(lessonId)
            ?? throw new KeyNotFoundException("Không tìm thấy buổi học.");

        // Ownership check
        if (userRole == UserRole.Tutor)
        {
            if (lesson.Tutorid != userId)
                throw new UnauthorizedAccessException("Bạn không có quyền hủy buổi học này.");
        }
        else if (userRole == UserRole.Parent)
        {
            var studentIds = await _studentRepo.GetStudentIdsByParentIdAsync(userId);
            if (lesson.Studentid == null || !studentIds.Contains(lesson.Studentid))
                throw new UnauthorizedAccessException("Bạn không có quyền hủy buổi học này.");
        }
        else
        {
            // Student
            var linkedUserId = lesson.Booking?.Student?.Linkeduserid;
            if (lesson.Studentid != userId && linkedUserId != userId)
                throw new UnauthorizedAccessException("Bạn không có quyền hủy buổi học này.");
        }

        // Only scheduled lessons can be cancelled
        if (lesson.Status != Scheduled)
            throw new InvalidOperationException(
                $"Không thể hủy buổi học ở trạng thái '{lesson.Status}'. Chỉ có thể hủy buổi học đang được lên lịch.");

        lesson.Status = Cancelled;
        await _lessonRepo.SaveChangesAsync();

        // Notify the other party
        var lessonTimeVn = lesson.Scheduledstart.ToString("dd/MM/yyyy HH:mm");
        var reasonSuffix = string.IsNullOrWhiteSpace(reason) ? "" : $" Lý do: {reason}";

        if (userRole != UserRole.Tutor && lesson.Tutorid != null)
        {
            await _notificationService.CreateNotificationAsync(new NotificationRequest
            {
                Userid  = lesson.Tutorid,
                Title   = "Buổi học bị hủy",
                Message = $"Phụ huynh/Học sinh đã hủy buổi học ngày {lessonTimeVn}.{reasonSuffix}"
            });
        }

        if (userRole == UserRole.Tutor && lesson.Booking?.Parentid != null)
        {
            await _notificationService.CreateNotificationAsync(new NotificationRequest
            {
                Userid  = lesson.Booking.Parentid,
                Title   = "Buổi học bị hủy",
                Message = $"Gia sư đã hủy buổi học ngày {lessonTimeVn}.{reasonSuffix}"
            });
        }

        _logger.LogInformation("Lesson {LessonId} cancelled by {UserId} ({Role})", lessonId, userId, userRole);
        return MapToLessonResponse(lesson);
    }

    public async Task<(IReadOnlyList<StudentLessonSummaryResponse> Items, int TotalCount)> GetStudentLessonsAsync(
        string studentProfileId, int page, int pageSize, string? status)
    {
        var (items, total) = await _lessonRepo.GetStudentLessonsPagedAsync(studentProfileId, page, pageSize, status);
        return (items, total);
    }

    public Task<StudentLessonDetailResponse?> GetStudentLessonDetailAsync(int lessonId, string studentProfileId)
        => _lessonRepo.GetStudentLessonDetailAsync(lessonId, studentProfileId);

    public Task<IReadOnlyList<StudentLessonSummaryResponse>> GetStudentPendingLessonsAsync(string studentProfileId)
        => _lessonRepo.GetStudentPendingLessonsAsync(studentProfileId);
}
