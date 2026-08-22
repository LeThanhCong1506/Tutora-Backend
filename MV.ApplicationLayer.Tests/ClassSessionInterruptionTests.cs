using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.InMemory.Diagnostics.Internal;
using Microsoft.Extensions.Logging.Abstractions;
using MV.ApplicationLayer.Helpers;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.ApplicationLayer.Services;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Entities;
using MV.DomainLayer.Exceptions;
using MV.DomainLayer.Helpers;
using MV.InfrastructureLayer.DBContext;
using Xunit;

namespace MV.ApplicationLayer.Tests;

public class ClassSessionInterruptionPolicyTests
{
    [Theory]
    [InlineData(0.49, false)]
    [InlineData(0.50, true)]
    [InlineData(0.75, true)]
    public void MeetsThreshold_DefaultSession_Requires50Percent(double overlapRatio, bool expected)
    {
        Assert.Equal(expected, ClassSessionInterruptionPolicy.MeetsThreshold(isFirstSessionOfBooking: false, overlapRatio));
    }

    [Theory]
    [InlineData(0.19, false)]
    [InlineData(0.20, true)]
    [InlineData(0.99, true)]
    public void MeetsThreshold_FirstSessionOfBooking_Requires20Percent(double overlapRatio, bool expected)
    {
        Assert.Equal(expected, ClassSessionInterruptionPolicy.MeetsThreshold(isFirstSessionOfBooking: true, overlapRatio));
    }

    [Fact]
    public void ComputeContinuationDuration_DefaultSession_IsHalfOfOriginal()
    {
        var start = new DateTime(2026, 8, 20, 8, 0, 0, DateTimeKind.Utc);
        var end = start.AddMinutes(90);

        var duration = ClassSessionInterruptionPolicy.ComputeContinuationDuration(false, start, end);

        Assert.Equal(TimeSpan.FromMinutes(45), duration);
    }

    [Fact]
    public void ComputeContinuationDuration_FirstSessionOfBooking_Is80PercentOfOriginal()
    {
        var start = new DateTime(2026, 8, 20, 8, 0, 0, DateTimeKind.Utc);
        var end = start.AddMinutes(100);

        var duration = ClassSessionInterruptionPolicy.ComputeContinuationDuration(true, start, end);

        Assert.Equal(TimeSpan.FromMinutes(80), duration);
    }

    [Fact]
    public void BuildContinuationSession_CopiesBookingTutorStudent_AndFlagsAsContinuation()
    {
        var now = new DateTime(2026, 8, 20, 10, 0, 0, DateTimeKind.Utc);
        var original = new ClassSession
        {
            Classsessionid = 42,
            Bookingid = 7,
            Tutorid = "tutor-1",
            Studentid = "student-profile-1",
            Scheduledstart = now.AddHours(-1),
            Scheduledend = now.AddMinutes(-30),
        };

        var continuation = ClassSessionInterruptionPolicy.BuildContinuationSession(original, isFirstSessionOfBooking: false, now);

        Assert.True(continuation.Iscontinuation);
        Assert.False(continuation.Isdisputerelearn);
        Assert.Equal(original.Classsessionid, continuation.Originalsessionid);
        Assert.Equal(original.Bookingid, continuation.Bookingid);
        Assert.Equal(original.Tutorid, continuation.Tutorid);
        Assert.Equal(original.Studentid, continuation.Studentid);
        Assert.Equal(0m, continuation.Lessonprice);
        Assert.Equal(ClassSessionStatus.Scheduled, continuation.Status);
        Assert.Equal(now.AddHours(1), continuation.Scheduledstart);
        // Buổi gốc dài 30 phút -> buổi phụ (50% còn lại) dài 15 phút.
        Assert.Equal(TimeSpan.FromMinutes(15), continuation.Scheduledend - continuation.Scheduledstart);
    }

    [Fact]
    public async Task IsFirstOriginalSessionAsync_ReturnsTrue_WhenNoEarlierOriginalSessionExists()
    {
        await using var db = CreateContext();
        var session = new ClassSession { Classsessionid = 1, Bookingid = 1, Scheduledstart = DateTime.UtcNow };
        db.ClassSessions.Add(session);
        await db.SaveChangesAsync();

        Assert.True(await ClassSessionInterruptionPolicy.IsFirstOriginalSessionAsync(db, session));
    }

    [Fact]
    public async Task IsFirstOriginalSessionAsync_ReturnsFalse_WhenAnEarlierOriginalSessionExists()
    {
        await using var db = CreateContext();
        var earlier = new ClassSession { Classsessionid = 1, Bookingid = 1, Scheduledstart = DateTime.UtcNow.AddDays(-7) };
        var later = new ClassSession { Classsessionid = 2, Bookingid = 1, Scheduledstart = DateTime.UtcNow };
        db.ClassSessions.AddRange(earlier, later);
        await db.SaveChangesAsync();

        Assert.False(await ClassSessionInterruptionPolicy.IsFirstOriginalSessionAsync(db, later));
    }

    [Fact]
    public async Task IsFirstOriginalSessionAsync_IgnoresEarlierMakeupAndContinuationRows()
    {
        await using var db = CreateContext();
        var earlierMakeup = new ClassSession { Classsessionid = 1, Bookingid = 1, Scheduledstart = DateTime.UtcNow.AddDays(-7), Ismakeup = true };
        var earlierContinuation = new ClassSession { Classsessionid = 2, Bookingid = 1, Scheduledstart = DateTime.UtcNow.AddDays(-6), Iscontinuation = true };
        var current = new ClassSession { Classsessionid = 3, Bookingid = 1, Scheduledstart = DateTime.UtcNow };
        db.ClassSessions.AddRange(earlierMakeup, earlierContinuation, current);
        await db.SaveChangesAsync();

        Assert.True(await ClassSessionInterruptionPolicy.IsFirstOriginalSessionAsync(db, current));
    }

    private static AgoraDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AgoraDbContext>()
            .UseInMemoryDatabase($"interruption-policy-{Guid.NewGuid()}")
            .Options;
        return new InterruptionPolicyTestDbContext(options);
    }

    private sealed class InterruptionPolicyTestDbContext(DbContextOptions<AgoraDbContext> options)
        : AgoraDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<QuestionBank>().Ignore(x => x.Embedding);
            modelBuilder.Entity<TutoraKbChunk>().Ignore(x => x.Embedding);
        }
    }
}

public class ClassSessionServiceRequestInterruptionTests
{
    private const string TutorId = "tutor-1";
    private const string StudentUserId = "student-user-1";
    private const string ParentId = "parent-1";
    private const int SessionId = 1;
    private const int BookingId = 1;

    [Fact]
    public async Task BelowThreshold_Throws_AndDoesNotMutateSessionOrCreateContinuation()
    {
        await using var db = CreateContext();
        // Không phải buổi đầu tiên của booking -> áp ngưỡng 50% mặc định, không phải 20%.
        SeedInProgressSession(db, scheduledMinutesAgo: 60, scheduledDurationMinutes: 60, addEarlierSiblingSession: true);
        await db.SaveChangesAsync();
        var service = CreateService(db, overlapRatio: 0.30); // dưới ngưỡng 50%

        var ex = await Assert.ThrowsAsync<ClassSessionException>(
            () => service.RequestInterruptionAsync(SessionId, TutorId, "kẹt xe"));
        Assert.Equal(ClassSessionErrorCodes.InterruptionThresholdNotMet, ex.ErrorCode);

        db.ChangeTracker.Clear();
        var session = await db.ClassSessions.SingleAsync(x => x.Classsessionid == SessionId);
        Assert.Equal(ClassSessionStatus.InProgress, session.Status);
        Assert.False(await db.ClassSessions.AnyAsync(x => x.Originalsessionid == SessionId));
    }

    [Fact]
    public async Task AtOrAboveThreshold_MarksOriginalInterrupted_AndCreatesContinuationSession()
    {
        await using var db = CreateContext();
        // Không phải buổi đầu tiên của booking -> áp ngưỡng 50% mặc định, không phải 20%.
        SeedInProgressSession(db, scheduledMinutesAgo: 60, scheduledDurationMinutes: 60, addEarlierSiblingSession: true);
        await db.SaveChangesAsync();
        var service = CreateService(db, overlapRatio: 0.60);

        var result = await service.RequestInterruptionAsync(SessionId, TutorId, "gia sư có việc đột xuất");

        Assert.NotNull(result);
        db.ChangeTracker.Clear();
        var original = await db.ClassSessions.SingleAsync(x => x.Classsessionid == SessionId);
        Assert.Equal(ClassSessionStatus.Interrupted, original.Status);
        Assert.NotNull(original.Interruptedat);
        Assert.Equal("gia sư có việc đột xuất", original.Interruptreason);

        var continuation = await db.ClassSessions.SingleAsync(x => x.Originalsessionid == SessionId);
        Assert.True(continuation.Iscontinuation);
        Assert.Equal(BookingId, continuation.Bookingid);
        Assert.Equal(TutorId, continuation.Tutorid);
        Assert.Equal(ClassSessionStatus.Scheduled, continuation.Status);
        // Buổi thường: ngưỡng 50% -> buổi phụ dài 50% x 60 phút = 30 phút.
        Assert.Equal(TimeSpan.FromMinutes(30), continuation.Scheduledend - continuation.Scheduledstart);
    }

    [Fact]
    public async Task FirstSessionOfBooking_UsesLowerTwentyPercentThreshold()
    {
        await using var db = CreateContext();
        SeedInProgressSession(db, scheduledMinutesAgo: 60, scheduledDurationMinutes: 60);
        await db.SaveChangesAsync();
        // 25% sẽ KHÔNG đạt ngưỡng buổi thường (50%) nhưng ĐẠT ngưỡng buổi đầu tiên (20%).
        var service = CreateService(db, overlapRatio: 0.25);

        var result = await service.RequestInterruptionAsync(SessionId, TutorId, null);

        Assert.NotNull(result);
        var continuation = await db.ClassSessions.SingleAsync(x => x.Originalsessionid == SessionId);
        // Buổi đầu tiên: ngưỡng 20% -> buổi phụ dài 80% x 60 phút = 48 phút.
        Assert.Equal(TimeSpan.FromMinutes(48), continuation.Scheduledend - continuation.Scheduledstart);
    }

    [Fact]
    public async Task AtOrAboveThreshold_NotifiesTutorStudentAndParent_AboutTheContinuationSession()
    {
        await using var db = CreateContext();
        SeedInProgressSession(db, scheduledMinutesAgo: 60, scheduledDurationMinutes: 60);
        await db.SaveChangesAsync();
        var notifications = new RecordingNotificationService();
        var service = CreateService(db, overlapRatio: 0.90, notificationService: notifications);

        await service.RequestInterruptionAsync(SessionId, TutorId, "mất điện");

        var continuation = await db.ClassSessions.SingleAsync(x => x.Originalsessionid == SessionId);
        var recipients = notifications.SentRequests.Select(r => r.Userid).OrderBy(x => x).ToList();
        Assert.Equal(new[] { ParentId, StudentUserId, TutorId }.OrderBy(x => x), recipients);
        Assert.All(notifications.SentRequests, r =>
        {
            Assert.Equal(NotificationType.LessonContinuationCreated, r.Type);
            Assert.Equal(continuation.Classsessionid.ToString(), r.Referenceid);
        });
    }

    [Fact]
    public async Task BelowThreshold_DoesNotSendAnyNotification()
    {
        await using var db = CreateContext();
        SeedInProgressSession(db, scheduledMinutesAgo: 60, scheduledDurationMinutes: 60, addEarlierSiblingSession: true);
        await db.SaveChangesAsync();
        var notifications = new RecordingNotificationService();
        var service = CreateService(db, overlapRatio: 0.10, notificationService: notifications);

        await Assert.ThrowsAsync<ClassSessionException>(() => service.RequestInterruptionAsync(SessionId, TutorId, null));

        Assert.Empty(notifications.SentRequests);
    }

    [Fact]
    public async Task OnContinuationSession_Throws()
    {
        await using var db = CreateContext();
        SeedInProgressSession(db, scheduledMinutesAgo: 60, scheduledDurationMinutes: 60, isContinuation: true);
        await db.SaveChangesAsync();
        var service = CreateService(db, overlapRatio: 1.0);

        var ex = await Assert.ThrowsAsync<ClassSessionException>(
            () => service.RequestInterruptionAsync(SessionId, TutorId, null));
        Assert.Equal(ClassSessionErrorCodes.AlreadyContinuationSession, ex.ErrorCode);
    }

    [Fact]
    public async Task WrongStatus_Throws()
    {
        await using var db = CreateContext();
        SeedInProgressSession(db, scheduledMinutesAgo: 60, scheduledDurationMinutes: 60, status: ClassSessionStatus.Scheduled);
        await db.SaveChangesAsync();
        var service = CreateService(db, overlapRatio: 1.0);

        var ex = await Assert.ThrowsAsync<ClassSessionException>(
            () => service.RequestInterruptionAsync(SessionId, TutorId, null));
        Assert.Equal(ClassSessionErrorCodes.InvalidClassSessionStatus, ex.ErrorCode);
    }

    [Fact]
    public async Task UnauthorizedUser_Throws()
    {
        await using var db = CreateContext();
        SeedInProgressSession(db, scheduledMinutesAgo: 60, scheduledDurationMinutes: 60);
        await db.SaveChangesAsync();
        var service = CreateService(db, overlapRatio: 1.0);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => service.RequestInterruptionAsync(SessionId, "some-other-user", null));
    }

    [Theory]
    [InlineData(StudentUserId)]
    [InlineData(ParentId)]
    public async Task StudentOrParentCanAlsoTriggerInterruption(string requestingUserId)
    {
        await using var db = CreateContext();
        SeedInProgressSession(db, scheduledMinutesAgo: 60, scheduledDurationMinutes: 60);
        await db.SaveChangesAsync();
        var service = CreateService(db, overlapRatio: 1.0);

        var result = await service.RequestInterruptionAsync(SessionId, requestingUserId, null);

        Assert.NotNull(result);
    }

    private static void SeedInProgressSession(
        AgoraDbContext db,
        int scheduledMinutesAgo,
        int scheduledDurationMinutes,
        bool isContinuation = false,
        string status = ClassSessionStatus.InProgress,
        bool addEarlierSiblingSession = false)
    {
        var tutorUser = new User { Userid = TutorId, Username = TutorId, Password = "x", Email = "tutor@test.local", Fullname = "Gia sư", Primaryrole = UserRole.Tutor };
        var parentUser = new User { Userid = ParentId, Username = ParentId, Password = "x", Email = "parent@test.local", Fullname = "Phụ huynh", Primaryrole = UserRole.Parent };
        var studentUser = new User { Userid = StudentUserId, Username = StudentUserId, Password = "x", Email = "student@test.local", Fullname = "Học sinh", Primaryrole = UserRole.Student };
        var tutor = new Tutorprofile { Tutorid = TutorId, Tutor = tutorUser };
        var student = new Studentprofile
        {
            Studentid = "student-profile-1",
            Parentid = ParentId,
            Parent = parentUser,
            Linkeduserid = StudentUserId,
            Linkeduser = studentUser,
            Fullname = studentUser.Fullname,
        };
        var booking = new Booking
        {
            Bookingid = BookingId,
            Parentid = ParentId,
            Parent = parentUser,
            Studentid = student.Studentid,
            Student = student,
            Tutorid = TutorId,
            Tutor = tutor
        };
        var scheduledStart = DateTime.UtcNow.AddMinutes(-scheduledMinutesAgo);
        var session = new ClassSession
        {
            Classsessionid = SessionId,
            Bookingid = booking.Bookingid,
            Booking = booking,
            Tutorid = TutorId,
            Tutor = tutor,
            Studentid = student.Studentid,
            Student = student,
            Scheduledstart = scheduledStart,
            Scheduledend = scheduledStart.AddMinutes(scheduledDurationMinutes),
            Status = status,
            Iscontinuation = isContinuation,
            Checkintime = DateTime.UtcNow.AddMinutes(-scheduledMinutesAgo),
            Realstart = DateTime.UtcNow.AddMinutes(-scheduledMinutesAgo),
        };
        db.Users.AddRange(tutorUser, parentUser, studentUser);
        db.Tutorprofiles.Add(tutor);
        db.Studentprofiles.Add(student);
        db.Bookings.Add(booking);
        db.ClassSessions.Add(session);

        // Buổi trước đó cùng booking, đã hoàn thành từ lâu — chỉ để buổi đang test không còn được
        // tính là "buổi đầu tiên của booking" (IsFirstOriginalSessionAsync sẽ trả về false).
        if (addEarlierSiblingSession)
        {
            db.ClassSessions.Add(new ClassSession
            {
                Classsessionid = SessionId + 1000,
                Bookingid = booking.Bookingid,
                Tutorid = TutorId,
                Studentid = student.Studentid,
                Scheduledstart = scheduledStart.AddDays(-7),
                Scheduledend = scheduledStart.AddDays(-7).AddMinutes(scheduledDurationMinutes),
                Status = ClassSessionStatus.Completed
            });
        }
    }

    private static ClassSessionService CreateService(AgoraDbContext db, double overlapRatio, INotificationService? notificationService = null)
    {
        var rescheduleProposalService = new ClassSessionRescheduleProposalService(
            db, null!, null!, NullLogger<ClassSessionRescheduleProposalService>.Instance);

        return new ClassSessionService(
            classSessionRepo: null!,
            bookingRepo: null!,
            studentRepo: null!,
            context: db,
            chatService: null!,
            notificationService: notificationService ?? new RecordingNotificationService(),
            zaloOAService: null!,
            storageService: null!,
            presence: null!,
            cloudRecording: new DisabledCloudRecordingService(),
            settlementService: null!,
            warningService: null!,
            recordingAccessTokenService: null!,
            backgroundJobClient: null!,
            rescheduleProposalService: rescheduleProposalService,
            sessionLogService: new FakeSessionLogService(overlapRatio),
            logger: NullLogger<ClassSessionService>.Instance);
    }

    private static AgoraDbContext CreateContext()
    {
        // InMemory provider không hỗ trợ transaction thật — RequestInterruptionAsync mở
        // BeginTransactionAsync() nên phải bỏ qua cảnh báo này (mặc định ném exception).
        var options = new DbContextOptionsBuilder<AgoraDbContext>()
            .UseInMemoryDatabase($"request-interruption-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new RequestInterruptionTestDbContext(options);
    }

    private sealed class RequestInterruptionTestDbContext(DbContextOptions<AgoraDbContext> options)
        : AgoraDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<QuestionBank>().Ignore(x => x.Embedding);
            modelBuilder.Entity<TutoraKbChunk>().Ignore(x => x.Embedding);
        }
    }

    /// <summary>Cloud Recording tắt (Enabled=false) -> TryStopRecordingAsync no-op, các method khác
    /// không bao giờ được gọi trong luồng RequestInterruptionAsync.</summary>
    private sealed class DisabledCloudRecordingService : ICloudRecordingService
    {
        public bool Enabled => false;
        public Task<CloudRecordingHandle> StartAsync(int classSessionId, string channel, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<CloudRecordingResult> StopAsync(int classSessionId, string channel, string resourceId, string sid, CancellationToken ct = default)
            => throw new NotImplementedException();
    }

    /// <summary>Trả về OverlapRatio cố định do test khai báo, không cần dữ liệu Agora thật.</summary>
    private sealed class FakeSessionLogService(double overlapRatio) : ISessionLogService
    {
        public Task RecordLobbyJoinAsync(int classSessionId, string appUserId, string role, string connectionId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task RecordLobbyHeartbeatAsync(int classSessionId, string appUserId, string connectionId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task CloseLobbyVisitAsync(int classSessionId, string appUserId, string connectionId, string closedReason, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<SessionLogResponse?> GetSessionLogAsync(int classSessionId, CancellationToken cancellationToken = default)
            => Task.FromResult<SessionLogResponse?>(new SessionLogResponse
            {
                ClassSessionId = classSessionId,
                Summary = new SessionLogSummary { OverlapRatio = overlapRatio }
            });

        public Task<TutorReliabilityResponse> GetTutorReliabilityAsync(string tutorUserId, DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task RecordAdmissionAsync(int classSessionId, string appUserId, string role, SessionAdmissionContext? context = null, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task RecordHeartbeatAsync(int classSessionId, string appUserId, string role, LiveSessionActivityReport? activity, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task CloseHeartbeatAsync(int classSessionId, string appUserId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}

/// <summary>
/// B4: job tự động đóng buổi Interrupted đã qua nửa đêm của ngày bị ngắt. Test chỉ xác minh logic
/// điều phối của ClassSessionService (chọn đúng buổi quá hạn, gọi settle đúng id, dọn buổi phụ chưa
/// dùng) — KHÔNG xác minh hành vi bên trong SettleDisputedClassSessionAsync (đã có sẵn, dùng chung
/// với luồng dispute, không thuộc phạm vi B4) nên dùng fake ISettlementService chỉ ghi lại lời gọi.
/// </summary>
public class ClassSessionAutoCloseInterruptedSessionsTests
{
    private const string TutorId = "tutor-1";
    private const int BookingId = 1;

    [Fact]
    public async Task NoInterruptedSessions_ReturnsZero()
    {
        await using var db = CreateContext();
        var settlement = new RecordingSettlementService();
        var service = CreateService(db, settlement);

        var closedCount = await service.AutoCloseExpiredInterruptedSessionsAsync();

        Assert.Equal(0, closedCount);
        Assert.Empty(settlement.SettledClassSessionIds);
    }

    [Fact]
    public async Task InterruptedSession_StillWithinSameDay_IsNotClosed()
    {
        await using var db = CreateContext();
        var original = SeedInterruptedSession(db, interruptedAt: DateTime.UtcNow);
        await db.SaveChangesAsync();
        var settlement = new RecordingSettlementService();
        var service = CreateService(db, settlement);

        var closedCount = await service.AutoCloseExpiredInterruptedSessionsAsync();

        Assert.Equal(0, closedCount);
        Assert.Empty(settlement.SettledClassSessionIds);
        db.ChangeTracker.Clear();
        Assert.Equal(ClassSessionStatus.Interrupted, (await db.ClassSessions.SingleAsync(x => x.Classsessionid == original.Classsessionid)).Status);
    }

    [Fact]
    public async Task InterruptedSession_PastMidnightOfInterruptedDay_IsClosed_AndSettleCalledWithCorrectId()
    {
        await using var db = CreateContext();
        // Ngắt "hôm qua" (tương đối theo giờ thực khi test chạy) -> chắc chắn đã qua nửa đêm.
        var original = SeedInterruptedSession(db, interruptedAt: DateTime.UtcNow.AddDays(-1));
        await db.SaveChangesAsync();
        var settlement = new RecordingSettlementService();
        var service = CreateService(db, settlement);

        var closedCount = await service.AutoCloseExpiredInterruptedSessionsAsync();

        Assert.Equal(1, closedCount);
        Assert.Equal([original.Classsessionid], settlement.SettledClassSessionIds);
    }

    [Fact]
    public async Task ExpiredInterruption_CancelsUnusedScheduledContinuation()
    {
        await using var db = CreateContext();
        var original = SeedInterruptedSession(db, interruptedAt: DateTime.UtcNow.AddDays(-1));
        var continuation = new ClassSession
        {
            Classsessionid = original.Classsessionid + 1000,
            Bookingid = BookingId,
            Tutorid = TutorId,
            Studentid = original.Studentid,
            Iscontinuation = true,
            Originalsessionid = original.Classsessionid,
            Status = ClassSessionStatus.Scheduled,
            Scheduledstart = DateTime.UtcNow.AddMinutes(-30),
            Scheduledend = DateTime.UtcNow.AddMinutes(30),
        };
        db.ClassSessions.Add(continuation);
        await db.SaveChangesAsync();
        var service = CreateService(db, new RecordingSettlementService());

        await service.AutoCloseExpiredInterruptedSessionsAsync();

        db.ChangeTracker.Clear();
        var reloaded = await db.ClassSessions.SingleAsync(x => x.Classsessionid == continuation.Classsessionid);
        Assert.Equal(ClassSessionStatus.Cancelled, reloaded.Status);
    }

    [Fact]
    public async Task ExpiredInterruption_LeavesAlreadyCompletedContinuationUntouched()
    {
        await using var db = CreateContext();
        var original = SeedInterruptedSession(db, interruptedAt: DateTime.UtcNow.AddDays(-1));
        var continuation = new ClassSession
        {
            Classsessionid = original.Classsessionid + 1000,
            Bookingid = BookingId,
            Tutorid = TutorId,
            Studentid = original.Studentid,
            Iscontinuation = true,
            Originalsessionid = original.Classsessionid,
            Status = ClassSessionStatus.Completed, // đã hoàn tất trước khi job này chạy
            Scheduledstart = DateTime.UtcNow.AddMinutes(-30),
            Scheduledend = DateTime.UtcNow.AddMinutes(30),
        };
        db.ClassSessions.Add(continuation);
        await db.SaveChangesAsync();
        var service = CreateService(db, new RecordingSettlementService());

        await service.AutoCloseExpiredInterruptedSessionsAsync();

        db.ChangeTracker.Clear();
        var reloaded = await db.ClassSessions.SingleAsync(x => x.Classsessionid == continuation.Classsessionid);
        Assert.Equal(ClassSessionStatus.Completed, reloaded.Status);
    }

    [Fact]
    public async Task MultipleExpiredInterruptions_AreAllClosed_AndCountedCorrectly()
    {
        await using var db = CreateContext();
        var first = SeedInterruptedSession(db, interruptedAt: DateTime.UtcNow.AddDays(-2));
        await db.SaveChangesAsync(); // lưu trước để lần seed thứ 2 không cố Add trùng User/Booking đã có
        var second = SeedInterruptedSession(db, interruptedAt: DateTime.UtcNow.AddDays(-1), idOffset: 2000);
        await db.SaveChangesAsync();
        var settlement = new RecordingSettlementService();
        var service = CreateService(db, settlement);

        var closedCount = await service.AutoCloseExpiredInterruptedSessionsAsync();

        Assert.Equal(2, closedCount);
        Assert.Equal(
            new[] { first.Classsessionid, second.Classsessionid }.OrderBy(x => x),
            settlement.SettledClassSessionIds.OrderBy(x => x));
    }

    [Fact]
    public async Task ExpiredInterruption_NotifiesTutorStudentAndParent()
    {
        await using var db = CreateContext();
        var original = SeedInterruptedSession(db, interruptedAt: DateTime.UtcNow.AddDays(-1), withParentAndStudent: true);
        await db.SaveChangesAsync();
        var notifications = new RecordingNotificationService();
        var service = CreateService(db, new RecordingSettlementService(), notifications);

        await service.AutoCloseExpiredInterruptedSessionsAsync();

        var recipients = notifications.SentRequests.Select(r => r.Userid).OrderBy(x => x).ToList();
        Assert.Equal(new[] { ParentId, StudentUserId, TutorId }.OrderBy(x => x), recipients);
        Assert.All(notifications.SentRequests, r =>
        {
            Assert.Equal(NotificationType.LessonInterruptionAutoClosed, r.Type);
            Assert.Equal(original.Classsessionid.ToString(), r.Referenceid);
        });
    }

    private const string StudentUserId = "student-user-1";
    private const string ParentId = "parent-1";

    private static ClassSession SeedInterruptedSession(
        AgoraDbContext db, DateTime interruptedAt, int idOffset = 1000, bool withParentAndStudent = false)
    {
        var id = idOffset;
        if (!db.Users.Any(u => u.Userid == TutorId))
        {
            db.Users.Add(new User { Userid = TutorId, Username = TutorId, Password = "x", Email = "tutor@test.local", Fullname = "Gia sư", Primaryrole = UserRole.Tutor });
            db.Tutorprofiles.Add(new Tutorprofile { Tutorid = TutorId });
        }

        Studentprofile? student = null;
        if (withParentAndStudent && !db.Studentprofiles.Any(s => s.Studentid == "student-profile-1"))
        {
            db.Users.Add(new User { Userid = ParentId, Username = ParentId, Password = "x", Email = "parent@test.local", Fullname = "Phụ huynh", Primaryrole = UserRole.Parent });
            db.Users.Add(new User { Userid = StudentUserId, Username = StudentUserId, Password = "x", Email = "student@test.local", Fullname = "Học sinh", Primaryrole = UserRole.Student });
            student = new Studentprofile { Studentid = "student-profile-1", Parentid = ParentId, Linkeduserid = StudentUserId };
            db.Studentprofiles.Add(student);
        }

        if (!db.Bookings.Any(b => b.Bookingid == BookingId))
            db.Bookings.Add(new Booking
            {
                Bookingid = BookingId,
                Tutorid = TutorId,
                Studentid = withParentAndStudent ? "student-profile-1" : null,
            });

        var session = new ClassSession
        {
            Classsessionid = id,
            Bookingid = BookingId,
            Tutorid = TutorId,
            Studentid = "student-profile-1",
            Status = ClassSessionStatus.Interrupted,
            Interruptedat = interruptedAt,
            Scheduledstart = interruptedAt.AddMinutes(-30),
            Scheduledend = interruptedAt.AddMinutes(30),
        };
        db.ClassSessions.Add(session);
        return session;
    }

    private static ClassSessionService CreateService(
        AgoraDbContext db, ISettlementService settlementService, INotificationService? notificationService = null)
        => new(
            classSessionRepo: null!,
            bookingRepo: null!,
            studentRepo: null!,
            context: db,
            chatService: null!,
            notificationService: notificationService ?? new RecordingNotificationService(),
            zaloOAService: null!,
            storageService: null!,
            presence: null!,
            cloudRecording: null!,
            settlementService: settlementService,
            warningService: null!,
            recordingAccessTokenService: null!,
            backgroundJobClient: null!,
            rescheduleProposalService: null!,
            sessionLogService: null!,
            logger: NullLogger<ClassSessionService>.Instance);

    private static AgoraDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AgoraDbContext>()
            .UseInMemoryDatabase($"auto-close-interrupted-{Guid.NewGuid()}")
            .Options;
        return new AutoCloseInterruptedTestDbContext(options);
    }

    private sealed class AutoCloseInterruptedTestDbContext(DbContextOptions<AgoraDbContext> options)
        : AgoraDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<QuestionBank>().Ignore(x => x.Embedding);
            modelBuilder.Entity<TutoraKbChunk>().Ignore(x => x.Embedding);
        }
    }

    private sealed class RecordingSettlementService : ISettlementService
    {
        public List<int> SettledClassSessionIds { get; } = [];

        public Task<int> ProcessAutoConfirmAsync(CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<SettlementResultResponse> SettleClassSessionAsync(int classSessionId, string? confirmedBy = null)
            => throw new NotImplementedException();

        public Task<SettlementResultResponse> SettleDisputedClassSessionAsync(int classSessionId, string? confirmedBy = null)
        {
            SettledClassSessionIds.Add(classSessionId);
            return Task.FromResult(new SettlementResultResponse { ClassSessionId = classSessionId, Success = true });
        }

        public Task<SettlementResultResponse> ProcessRefundAsync(int classSessionId, int refundPercentage, string processedBy)
            => throw new NotImplementedException();

        public Task<RefundPreviewResponse> PreviewRefundAsync(int classSessionId, int refundPercentage)
            => throw new NotImplementedException();

        public Task<List<PendingClassSessionResponse>> GetPendingSettlementsAsync()
            => throw new NotImplementedException();

        public Task FinalizeBookingEarlyAsync(int bookingId, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<bool> FinalizeBookingEarlyByUserAsync(int bookingId, string userId, string? reason = null, CancellationToken ct = default)
            => throw new NotImplementedException();
    }
}

/// <summary>Ghi lại mọi NotificationRequest đã gửi (qua cả 2 hàm, đơn lẫn batch) để test assert đúng
/// người nhận/nội dung, thay vì chỉ xác nhận "không crash" như khi dùng null!. Dùng chung cho các
/// test class ở trên trong cùng file này (RequestInterruptionAsync và AutoCloseExpiredInterruptedSessionsAsync
/// đều bắn thông báo qua INotificationService).</summary>
internal sealed class RecordingNotificationService : INotificationService
{
    public List<NotificationRequest> SentRequests { get; } = [];

    public Task<StatusResponse> CreateNotificationAsync(NotificationRequest request)
    {
        SentRequests.Add(request);
        return Task.FromResult(new StatusResponse());
    }

    public Task<StatusResponse> CreateNotificationsAsync(IEnumerable<NotificationRequest> requests)
    {
        SentRequests.AddRange(requests);
        return Task.FromResult(new StatusResponse());
    }

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

/// <summary>
/// Gia sư/học sinh/phụ huynh cùng đồng ý bỏ buổi phụ (link 2) — mỗi phía xác nhận qua đúng cột
/// của mình (ConfirmSkipContinuationAsync), và chỉ khi CẢ HAI đã xác nhận thì SubmitReportAsync
/// trên buổi GỐC (đang interrupted) mới nhận báo cáo, đồng thời tự huỷ buổi phụ trong cùng
/// transaction. Trước khi có cơ chế này, buổi gốc bị ngắt không bao giờ nhận được báo cáo nữa vì
/// SubmitReportAsync chỉ nhận status in_progress còn buổi ngắt chuyển thẳng sang interrupted.
/// </summary>
public class ClassSessionSkipContinuationTests
{
    private const string TutorId = "tutor-1";
    private const string StudentUserId = "student-user-1";
    private const string ParentId = "parent-1";
    private const string StudentProfileId = "student-profile-1";
    private const int BookingId = 1;
    private const int OriginalSessionId = 1;
    private const int ContinuationSessionId = 2;

    [Fact]
    public async Task ConfirmByTutor_SetsOnlyTutorConfirmed()
    {
        await using var db = CreateContext();
        SeedChain(db);
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var result = await service.ConfirmSkipContinuationAsync(ContinuationSessionId, TutorId);

        Assert.True(result.TutorConfirmed);
        Assert.False(result.StudentConfirmed);
        Assert.False(result.BothConfirmed);
    }

    [Theory]
    [InlineData(StudentUserId)]
    [InlineData(ParentId)]
    public async Task ConfirmByStudentOrParent_AfterTutor_SetsBothConfirmed(string secondCaller)
    {
        await using var db = CreateContext();
        SeedChain(db);
        await db.SaveChangesAsync();
        var service = CreateService(db);

        await service.ConfirmSkipContinuationAsync(ContinuationSessionId, TutorId);
        var result = await service.ConfirmSkipContinuationAsync(ContinuationSessionId, secondCaller);

        Assert.True(result.TutorConfirmed);
        Assert.True(result.StudentConfirmed);
        Assert.True(result.BothConfirmed);
    }

    [Fact]
    public async Task ConfirmTwiceBySameSide_StaysConfirmed_DoesNotThrow()
    {
        await using var db = CreateContext();
        SeedChain(db);
        await db.SaveChangesAsync();
        var service = CreateService(db);

        await service.ConfirmSkipContinuationAsync(ContinuationSessionId, TutorId);
        var result = await service.ConfirmSkipContinuationAsync(ContinuationSessionId, TutorId);

        Assert.True(result.TutorConfirmed);
        Assert.False(result.BothConfirmed);
    }

    [Fact]
    public async Task ConfirmOnNonContinuationSession_Throws()
    {
        await using var db = CreateContext();
        SeedChain(db);
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var ex = await Assert.ThrowsAsync<ClassSessionException>(
            () => service.ConfirmSkipContinuationAsync(OriginalSessionId, TutorId));
        Assert.Equal(ClassSessionErrorCodes.InvalidClassSessionStatus, ex.ErrorCode);
    }

    [Fact]
    public async Task ConfirmAfterContinuationAlreadyStarted_Throws()
    {
        await using var db = CreateContext();
        SeedChain(db, continuationStatus: ClassSessionStatus.InProgress);
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var ex = await Assert.ThrowsAsync<ClassSessionException>(
            () => service.ConfirmSkipContinuationAsync(ContinuationSessionId, TutorId));
        Assert.Equal(ClassSessionErrorCodes.InvalidClassSessionStatus, ex.ErrorCode);
    }

    [Fact]
    public async Task ConfirmByUnrelatedUser_Throws()
    {
        await using var db = CreateContext();
        SeedChain(db);
        await db.SaveChangesAsync();
        var service = CreateService(db);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => service.ConfirmSkipContinuationAsync(ContinuationSessionId, "some-other-user"));
    }

    [Fact]
    public async Task SubmitReportOnInterruptedOriginal_WithoutBothSidesConfirmed_Throws()
    {
        await using var db = CreateContext();
        SeedChain(db);
        await db.SaveChangesAsync();
        var service = CreateService(db);
        // Chỉ 1 phía đồng ý — chưa đủ điều kiện.
        await service.ConfirmSkipContinuationAsync(ContinuationSessionId, TutorId);

        var ex = await Assert.ThrowsAsync<ClassSessionException>(
            () => service.SubmitReportAsync(OriginalSessionId, TutorId, MakeReportRequest()));
        Assert.Equal(ClassSessionErrorCodes.InvalidClassSessionStatus, ex.ErrorCode);
    }

    [Fact]
    public async Task SubmitReportOnInterruptedOriginal_AfterBothSidesConfirmSkip_Succeeds_AndCancelsContinuation()
    {
        await using var db = CreateContext();
        SeedChain(db);
        await db.SaveChangesAsync();
        var service = CreateService(db);
        await service.ConfirmSkipContinuationAsync(ContinuationSessionId, TutorId);
        await service.ConfirmSkipContinuationAsync(ContinuationSessionId, StudentUserId);

        var result = await service.SubmitReportAsync(OriginalSessionId, TutorId, MakeReportRequest());

        Assert.NotNull(result);
        db.ChangeTracker.Clear();
        var original = await db.ClassSessions.SingleAsync(x => x.Classsessionid == OriginalSessionId);
        Assert.Equal(ClassSessionStatus.PendingConfirmation, original.Status);
        Assert.Equal("Đã dạy 80% nội dung trước khi bị ngắt.", original.Lessoncontent);

        var continuation = await db.ClassSessions.SingleAsync(x => x.Classsessionid == ContinuationSessionId);
        Assert.Equal(ClassSessionStatus.Cancelled, continuation.Status);
    }

    private static SubmitReportRequest MakeReportRequest() => new()
    {
        ContentCovered = "Đã dạy 80% nội dung trước khi bị ngắt.",
        HomeworkAssigned = "",
        AttendanceNote = "",
        IsStudentPresent = true,
    };

    private static void SeedChain(AgoraDbContext db, string continuationStatus = ClassSessionStatus.Scheduled)
    {
        var tutorUser = new User { Userid = TutorId, Username = TutorId, Password = "x", Email = "tutor@test.local", Fullname = "Gia sư", Primaryrole = UserRole.Tutor };
        var parentUser = new User { Userid = ParentId, Username = ParentId, Password = "x", Email = "parent@test.local", Fullname = "Phụ huynh", Primaryrole = UserRole.Parent };
        var studentUser = new User { Userid = StudentUserId, Username = StudentUserId, Password = "x", Email = "student@test.local", Fullname = "Học sinh", Primaryrole = UserRole.Student };
        var tutor = new Tutorprofile { Tutorid = TutorId, Tutor = tutorUser };
        var student = new Studentprofile
        {
            Studentid = StudentProfileId,
            Parentid = ParentId,
            Parent = parentUser,
            Linkeduserid = StudentUserId,
            Linkeduser = studentUser,
            Fullname = studentUser.Fullname,
        };
        var booking = new Booking
        {
            Bookingid = BookingId,
            Parentid = ParentId,
            Parent = parentUser,
            Studentid = student.Studentid,
            Student = student,
            Tutorid = TutorId,
            Tutor = tutor,
            // Remainingpaidat khác null -> SubmitReportAsync không đi vào nhánh "báo cáo đầu tiên của
            // booking" (không liên quan tới phạm vi test này).
            Remainingpaidat = DateTime.UtcNow.AddDays(-30),
        };
        var scheduledStart = DateTime.UtcNow.AddHours(-2);
        var original = new ClassSession
        {
            Classsessionid = OriginalSessionId,
            Bookingid = BookingId,
            Booking = booking,
            Tutorid = TutorId,
            Tutor = tutor,
            Studentid = student.Studentid,
            Student = student,
            Status = ClassSessionStatus.Interrupted,
            Interruptedat = scheduledStart.AddMinutes(48),
            Scheduledstart = scheduledStart,
            Scheduledend = scheduledStart.AddMinutes(60),
            Checkintime = scheduledStart,
        };
        var continuation = new ClassSession
        {
            Classsessionid = ContinuationSessionId,
            Bookingid = BookingId,
            Booking = booking,
            Tutorid = TutorId,
            Tutor = tutor,
            Studentid = student.Studentid,
            Student = student,
            Iscontinuation = true,
            Originalsessionid = OriginalSessionId,
            Status = continuationStatus,
            Scheduledstart = DateTime.UtcNow.AddHours(1),
            Scheduledend = DateTime.UtcNow.AddHours(1).AddMinutes(12),
        };
        db.Users.AddRange(tutorUser, parentUser, studentUser);
        db.Tutorprofiles.Add(tutor);
        db.Studentprofiles.Add(student);
        db.Bookings.Add(booking);
        db.ClassSessions.AddRange(original, continuation);
    }

    private static ClassSessionService CreateService(AgoraDbContext db, INotificationService? notificationService = null)
    {
        // SubmitReportAsync/RequestInterruptionAsync đều gọi GetTutorClassSessionDetailAsync ở cuối
        // để build response, và hàm đó gọi thẳng rescheduleProposalService.GetProposalHistoryAsync —
        // không thể để null! như các dependency khác không được đụng tới trong 2 hàm đang test.
        var rescheduleProposalService = new ClassSessionRescheduleProposalService(
            db, null!, null!, NullLogger<ClassSessionRescheduleProposalService>.Instance);

        return new ClassSessionService(
            classSessionRepo: null!,
            bookingRepo: null!,
            studentRepo: null!,
            context: db,
            chatService: null!,
            notificationService: notificationService ?? new RecordingNotificationService(),
            zaloOAService: null!,
            storageService: null!,
            presence: null!,
            cloudRecording: null!,
            settlementService: null!,
            warningService: null!,
            recordingAccessTokenService: null!,
            backgroundJobClient: null!,
            rescheduleProposalService: rescheduleProposalService,
            sessionLogService: null!,
            logger: NullLogger<ClassSessionService>.Instance);
    }

    private static AgoraDbContext CreateContext()
    {
        // InMemory provider không hỗ trợ transaction thật — SubmitReportAsync mở
        // BeginTransactionAsync() nên phải bỏ qua cảnh báo này (mặc định ném exception).
        var options = new DbContextOptionsBuilder<AgoraDbContext>()
            .UseInMemoryDatabase($"skip-continuation-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new SkipContinuationTestDbContext(options);
    }

    private sealed class SkipContinuationTestDbContext(DbContextOptions<AgoraDbContext> options)
        : AgoraDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<QuestionBank>().Ignore(x => x.Embedding);
            modelBuilder.Entity<TutoraKbChunk>().Ignore(x => x.Embedding);
        }
    }
}
