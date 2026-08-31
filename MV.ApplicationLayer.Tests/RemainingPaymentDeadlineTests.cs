using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MV.ApplicationLayer.Helpers;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.ApplicationLayer.Services;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Entities;
using MV.InfrastructureLayer.DBContext;
using Xunit;

namespace MV.ApplicationLayer.Tests;

/// <summary>
/// Mục E1: RemainingPaymentDeadlinePolicy — hạn 48h thanh toán phần còn lại không được vượt quá
/// giờ học buổi reserved gần nhất - 2h. Đây là logic thuần, test trực tiếp không cần DB/service.
/// </summary>
public class RemainingPaymentDeadlinePolicyTests
{
    private static readonly DateTime Now = new(2026, 8, 20, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void NoReservedSession_UsesDefault48Hours()
    {
        var deadline = RemainingPaymentDeadlinePolicy.ComputeDeadline(Now, null);

        Assert.Equal(Now.AddHours(48), deadline);
    }

    [Fact]
    public void ReservedSessionFarAway_DoesNotShortenDeadline()
    {
        // Buổi kế tiếp còn 100h nữa -> xa hơn 48h, không cần chặn.
        var deadline = RemainingPaymentDeadlinePolicy.ComputeDeadline(Now, Now.AddHours(100));

        Assert.Equal(Now.AddHours(48), deadline);
    }

    [Fact]
    public void ReservedSessionWithinDefaultWindow_CapsToTwoHoursBeforeIt()
    {
        // Buổi kế tiếp còn 10h nữa -> hạn phải là 8h nữa (10h - buffer 2h), không phải 48h.
        var sessionStart = Now.AddHours(10);
        var deadline = RemainingPaymentDeadlinePolicy.ComputeDeadline(Now, sessionStart);

        Assert.Equal(sessionStart.AddHours(-2), deadline);
        Assert.Equal(Now.AddHours(8), deadline);
    }

    [Fact]
    public void ReservedSessionVeryClose_NeverProducesAPastDeadline()
    {
        // Buổi kế tiếp chỉ còn 30 phút nữa -> cap lý thuyết (start-2h) đã ở QUÁ KHỨ.
        // Không được trả về 1 hạn đã qua ngay lúc vừa kích hoạt -> phải kẹp về đúng `now`.
        var sessionStart = Now.AddMinutes(30);
        var deadline = RemainingPaymentDeadlinePolicy.ComputeDeadline(Now, sessionStart);

        Assert.Equal(Now, deadline);
    }

    [Fact]
    public void ReservedSessionExactlyAtBoundary_EqualsDefaultDeadline()
    {
        // Buổi kế tiếp đúng 50h nữa (= 48h default + 2h buffer) -> cap = default, không lệch.
        var sessionStart = Now.AddHours(50);
        var deadline = RemainingPaymentDeadlinePolicy.ComputeDeadline(Now, sessionStart);

        Assert.Equal(Now.AddHours(48), deadline);
    }
}

/// <summary>
/// Mục E2: PastDueSessionShiftPolicy — nếu ActivateRemainingSessionsAsync chạy trễ tới mức 1 buổi
/// reserved đã quá giờ, phải tự dời sang tương lai (+7 ngày mỗi vòng, giữ giờ + thời lượng) thay vì
/// kích hoạt mù với giờ cũ.
/// </summary>
public class PastDueSessionShiftPolicyTests
{
    private static readonly DateTime Now = new(2026, 8, 20, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void SessionPastDueByLessThanAWeek_ShiftsExactlySevenDays()
    {
        var start = Now.AddHours(-3);
        var end = Now.AddHours(-2); // 1 giờ, đã trôi qua 3h/2h trước

        var (newStart, newEnd) = PastDueSessionShiftPolicy.ShiftIntoFuture(start, end, Now);

        Assert.Equal(start.AddDays(7), newStart);
        Assert.Equal(end.AddDays(7), newEnd);
        Assert.True(newStart > Now);
        Assert.Equal(TimeSpan.FromHours(1), newEnd - newStart); // giữ nguyên thời lượng
    }

    [Fact]
    public void SessionPastDueByMoreThanAWeek_KeepsShiftingUntilInTheFuture()
    {
        // Quá hạn 20 ngày -> dời 1 lần (+7 ngày) vẫn còn ở quá khứ (13 ngày trước), phải dời tiếp.
        var start = Now.AddDays(-20);
        var end = start.AddMinutes(45);

        var (newStart, newEnd) = PastDueSessionShiftPolicy.ShiftIntoFuture(start, end, Now);

        Assert.True(newStart > Now, $"Kết quả {newStart:o} phải ở tương lai so với {Now:o}");
        Assert.Equal(TimeSpan.FromMinutes(45), newEnd - newStart);
        // Đúng bội số của 7 ngày kể từ giờ gốc.
        Assert.Equal(0, (newStart - start).Days % 7);
    }
}

/// <summary>
/// Mục E1 (kiểm tra nối dây thật): SubmitReportAsync — lần nộp báo cáo đầu tiên của booking phải
/// dùng đúng RemainingPaymentDeadlinePolicy khi đặt Booking.Paymentdueat, dựa trên buổi reserved
/// sớm nhất CÙNG booking (không lẫn buổi của booking khác).
/// </summary>
public class SubmitReportAsyncRemainingPaymentDeadlineTests
{
    private const string TutorId = "tutor-1";
    private const int BookingId = 1;
    private const int ReportedSessionId = 1;

    [Fact]
    public async Task FirstReport_CapsPaymentDueAt_ToNearestReservedSessionOfSameBooking()
    {
        await using var db = CreateContext();
        var now = DateTime.UtcNow;
        SeedBookingAndReportedSession(db);
        AddReservedSession(db, id: 2, bookingId: BookingId, scheduledStart: now.AddHours(10));
        // Buổi reserved của booking KHÁC, gần hơn nhiều -> không được ảnh hưởng tới booking này.
        AddOtherBookingReservedSession(db, id: 3, otherBookingId: 999, scheduledStart: now.AddMinutes(20));
        await db.SaveChangesAsync();
        var service = CreateService(db);

        await service.SubmitReportAsync(ReportedSessionId, TutorId, new SubmitReportRequest { ContentCovered = "Bài 1" });

        db.ChangeTracker.Clear();
        var booking = await db.Bookings.SingleAsync(b => b.Bookingid == BookingId);
        Assert.Equal(BookingStatus.PendingRemainingPayment, booking.Status);
        Assert.NotNull(booking.Paymentdueat);
        // Phải bám theo buổi reserved CÙNG booking (10h - 2h = 8h), không bị buổi booking khác (20 phút) kéo xuống.
        Assert.InRange(booking.Paymentdueat!.Value, now.AddHours(7).AddMinutes(55), now.AddHours(8).AddMinutes(5));
    }

    [Fact]
    public async Task FirstReport_NoOtherReservedSessions_UsesDefault48Hours()
    {
        await using var db = CreateContext();
        var now = DateTime.UtcNow;
        SeedBookingAndReportedSession(db);
        await db.SaveChangesAsync();
        var service = CreateService(db);

        await service.SubmitReportAsync(ReportedSessionId, TutorId, new SubmitReportRequest { ContentCovered = "Bài 1" });

        db.ChangeTracker.Clear();
        var booking = await db.Bookings.SingleAsync(b => b.Bookingid == BookingId);
        Assert.InRange(booking.Paymentdueat!.Value, now.AddHours(47).AddMinutes(55), now.AddHours(48).AddMinutes(5));
    }

    private static void SeedBookingAndReportedSession(AgoraDbContext db)
    {
        var tutorUser = new User { Userid = TutorId, Username = TutorId, Password = "x", Email = "tutor@test.local", Fullname = "Gia sư", Primaryrole = UserRole.Tutor };
        var tutor = new Tutorprofile { Tutorid = TutorId, Tutor = tutorUser };
        var booking = new Booking
        {
            Bookingid = BookingId,
            Tutorid = TutorId,
            Tutor = tutor,
            Status = BookingStatus.DepositPaid,
            Remainingpaidat = null,
        };
        var reportedSession = new ClassSession
        {
            Classsessionid = ReportedSessionId,
            Bookingid = BookingId,
            Booking = booking,
            Tutorid = TutorId,
            Status = ClassSessionStatus.InProgress,
            Scheduledstart = DateTime.UtcNow.AddHours(-1),
            Scheduledend = DateTime.UtcNow,
            Checkintime = DateTime.UtcNow.AddHours(-1),
        };
        db.Users.Add(tutorUser);
        db.Tutorprofiles.Add(tutor);
        db.Bookings.Add(booking);
        db.ClassSessions.Add(reportedSession);
    }

    private static void AddReservedSession(AgoraDbContext db, int id, int bookingId, DateTime scheduledStart)
    {
        db.ClassSessions.Add(new ClassSession
        {
            Classsessionid = id,
            Bookingid = bookingId,
            Tutorid = TutorId,
            Status = ClassSessionStatus.Reserved,
            Scheduledstart = scheduledStart,
            Scheduledend = scheduledStart.AddMinutes(60),
        });
    }

    private static void AddOtherBookingReservedSession(AgoraDbContext db, int id, int otherBookingId, DateTime scheduledStart)
    {
        db.Bookings.Add(new Booking { Bookingid = otherBookingId, Tutorid = TutorId, Status = BookingStatus.DepositPaid });
        db.ClassSessions.Add(new ClassSession
        {
            Classsessionid = id,
            Bookingid = otherBookingId,
            Tutorid = TutorId,
            Status = ClassSessionStatus.Reserved,
            Scheduledstart = scheduledStart,
            Scheduledend = scheduledStart.AddMinutes(60),
        });
    }

    private static ClassSessionService CreateService(AgoraDbContext db)
        => new(
            classSessionRepo: null!,
            bookingRepo: null!,
            studentRepo: null!,
            context: db,
            chatService: null!,
            notificationService: new NoOpNotificationService(),
            zaloOAService: null!,
            storageService: null!,
            presence: null!,
            cloudRecording: new DisabledCloudRecordingService(),
            settlementService: null!,
            warningService: null!,
            recordingAccessTokenService: null!,
            backgroundJobClient: null!,
            // SubmitReportAsync gọi GetTutorClassSessionDetailAsync ở cuối, hàm này gọi
            // GetProposalHistoryAsync (chỉ đọc DB, an toàn với notification/hub = null!).
            rescheduleProposalService: new ClassSessionRescheduleProposalService(
                db, null!, null!, NullLogger<ClassSessionRescheduleProposalService>.Instance),
            sessionLogService: null!,
            logger: NullLogger<ClassSessionService>.Instance);

    private static AgoraDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AgoraDbContext>()
            .UseInMemoryDatabase($"remaining-payment-deadline-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new RemainingPaymentDeadlineTestDbContext(options);
    }

    private sealed class RemainingPaymentDeadlineTestDbContext(DbContextOptions<AgoraDbContext> options)
        : AgoraDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<QuestionBank>().Ignore(x => x.Embedding);
            modelBuilder.Entity<TutoraKbChunk>().Ignore(x => x.Embedding);
        }
    }

    private sealed class DisabledCloudRecordingService : ICloudRecordingService
    {
        public bool Enabled => false;
        public bool AudioOnlyEnabled => false;
        public Task<CloudRecordingHandle> StartAsync(int classSessionId, string channel, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<CloudRecordingResult> StopAsync(int classSessionId, string channel, string resourceId, string sid, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<CloudRecordingHandle> StartAudioAsync(int classSessionId, string channel, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<CloudRecordingResult> StopAudioAsync(int classSessionId, string channel, string resourceId, string sid, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<CloudRecordingQueryResult> QueryAsync(string resourceId, string sid, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class NoOpNotificationService : INotificationService
    {
        public Task<StatusResponse> CreateNotificationAsync(NotificationRequest request) => Task.FromResult(new StatusResponse());
        public Task<StatusResponse> CreateNotificationsAsync(IEnumerable<NotificationRequest> requests) => throw new NotImplementedException();
        public Task<NotificationResponse?> GetNotificationByIdAsync(int notificationId) => throw new NotImplementedException();
        public Task<IEnumerable<NotificationResponse>> GetNotificationsByUserIdAsync(string userId) => throw new NotImplementedException();
        public Task<IEnumerable<NotificationResponse>> GetUnreadNotificationsByUserIdAsync(string userId) => throw new NotImplementedException();
        public Task<int> GetUnreadCountByUserIdAsync(string userId) => throw new NotImplementedException();
        public Task<UnreadCountResponse> GetUnreadCountResponseByUserIdAsync(string userId) => throw new NotImplementedException();
        public Task<IEnumerable<NotificationResponse>> GetAllNotificationsAsync() => throw new NotImplementedException();
        public Task<StatusResponse> MarkAsReadAsync(int notificationId, string currentUserId) => throw new NotImplementedException();
        public Task<StatusResponse> MarkAllAsReadAsync(string userId) => throw new NotImplementedException();
        public Task<StatusResponse> MarkAsReadByTypeAsync(string userId, string type) => throw new NotImplementedException();
        public Task<StatusResponse> DeleteNotificationAsync(int notificationId, string currentUserId) => throw new NotImplementedException();
        public Task<StatusResponse> DeleteAllNotificationsByUserIdAsync(string userId) => throw new NotImplementedException();
        public Task<StatusResponse> DeleteOldNotificationsAsync(int daysOld) => throw new NotImplementedException();
    }
}
