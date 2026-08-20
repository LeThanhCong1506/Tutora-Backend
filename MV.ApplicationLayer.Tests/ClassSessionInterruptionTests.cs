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

    private static ClassSessionService CreateService(AgoraDbContext db, double overlapRatio)
    {
        var rescheduleProposalService = new ClassSessionRescheduleProposalService(
            db, null!, null!, NullLogger<ClassSessionRescheduleProposalService>.Instance);

        return new ClassSessionService(
            classSessionRepo: null!,
            bookingRepo: null!,
            studentRepo: null!,
            context: db,
            chatService: null!,
            notificationService: null!,
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
