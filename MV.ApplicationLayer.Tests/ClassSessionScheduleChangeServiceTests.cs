using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MV.ApplicationLayer.Helpers;
using MV.ApplicationLayer.Services;
using MV.DomainLayer.Constants;
using MV.DomainLayer.Entities;
using MV.DomainLayer.Helpers;
using MV.InfrastructureLayer.DBContext;
using Xunit;

namespace MV.ApplicationLayer.Tests;

public class ClassSessionScheduleChangeServiceTests
{
    // Cơ chế xác nhận vào học ngoài giờ đã được khôi phục lại (từng bị gỡ theo yêu cầu sản phẩm,
    // nay khôi phục theo yêu cầu mới). Buổi CHÍNH (Iscontinuation=false) ngoài khung giờ bình
    // thường [Scheduledstart-15p, Scheduledend] phải tạo yêu cầu xác nhận trước khi được vào.
    [Fact]
    public async Task OutOfWindowSession_ForRegularSession_RequiresConfirmation()
    {
        await using var db = CreateContext();
        SeedSession(db, DateOnly.FromDateTime(TimeZoneHelper.UtcNow).AddYears(-15));
        await db.SaveChangesAsync();
        var service = CreateService(db);

        // SeedSession đặt Scheduledstart/Scheduledend trong quá khứ — ngoài "khung giờ bình
        // thường" nên bắt buộc đồng thuận trước khi cho vào.
        var tutorState = await service.GetOrCreateStateAsync(1, "tutor-1", UserRole.Tutor);
        Assert.True(tutorState.RequiresConfirmation);
        Assert.False(tutorState.AdmissionAllowed);

        Assert.NotEmpty(await db.ClassSessionScheduleChanges.ToListAsync());
    }

    // Buổi PHỤ (Iscontinuation=true) là ngoại lệ — luôn coi là trong khung giờ bình thường dù
    // Scheduledstart/end (chỉ là mốc ước tính lúc tạo) đã trôi qua từ lâu, nên không bao giờ cần
    // xác nhận — khớp lý do: buổi phụ phải học tiếp được bất cứ lúc nào tới hạn tự huỷ nửa đêm.
    [Fact]
    public async Task OutOfWindowSession_ForContinuationSession_NeverRequiresConfirmation()
    {
        await using var db = CreateContext();
        SeedSession(db, DateOnly.FromDateTime(TimeZoneHelper.UtcNow).AddYears(-15), isContinuation: true);
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var tutorState = await service.GetOrCreateStateAsync(1, "tutor-1", UserRole.Tutor);
        Assert.False(tutorState.RequiresConfirmation);
        Assert.True(tutorState.AdmissionAllowed);

        var studentState = await service.GetOrCreateStateAsync(1, "student-user-1", UserRole.Student);
        Assert.False(studentState.RequiresConfirmation);
        Assert.True(studentState.AdmissionAllowed);

        Assert.Empty(await db.ClassSessionScheduleChanges.ToListAsync());
    }

    [Fact]
    public async Task ParentDetailRead_DoesNotCreateAnOffScheduleRequest()
    {
        await using var db = CreateContext();
        SeedSession(db, DateOnly.FromDateTime(TimeZoneHelper.UtcNow).AddYears(-15));
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var state = await service.GetExistingStateAsync(1, "parent-1", UserRole.Parent);

        Assert.False(state.RequiresConfirmation);
        Assert.False(state.CanCurrentUserConfirm);
        Assert.Empty(await db.ClassSessionScheduleChanges.ToListAsync());
    }

    [Fact]
    public async Task AdminCanSupportWithoutCreatingOrConfirmingAChange()
    {
        await using var db = CreateContext();
        SeedSession(db, DateOnly.FromDateTime(TimeZoneHelper.UtcNow).AddYears(-15));
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var state = await service.GetOrCreateStateAsync(1, "admin-1", UserRole.Admin);

        Assert.False(state.RequiresConfirmation);
        Assert.True(state.AdmissionAllowed);
        Assert.Empty(await db.ClassSessionScheduleChanges.ToListAsync());
    }

    // Double-booking conflict detection (gia sư/học sinh đã có buổi khác trùng giờ) không còn đi
    // qua RespondAsync nữa (giờ luôn trả sớm vì RequiresConfirmation=false) — nó chạy độc lập ở
    // SessionLobbyPresenceBroadcaster.BroadcastAsync mỗi khi có người vào lobby, dùng chung
    // ClassSessionScheduleConflictGuard.FindAsync. Vẫn được phủ bởi 2 test dưới đây, test thẳng
    // guard đó thay vì qua đường RespondAsync đã không còn dùng tới nữa.
    [Fact]
    public async Task BoundaryTouchingSession_IsAllowed()
    {
        await using var db = CreateContext();
        SeedSession(db, DateOnly.FromDateTime(TimeZoneHelper.UtcNow).AddYears(-16), managedByParent: false);
        var candidateStart = TimeZoneHelper.UtcNow.AddHours(2);
        AddConflictSession(db, 2, "tutor-1", "another-student", candidateStart.AddHours(-1), candidateStart);
        await db.SaveChangesAsync();

        var conflict = await ClassSessionScheduleConflictGuard.FindAsync(
            db,
            1,
            "tutor-1",
            "student-profile-1",
            candidateStart,
            candidateStart.AddHours(1));

        Assert.Null(conflict);
    }

    [Fact]
    public async Task ApprovedChange_ReservesTutorIntervalUntilAppliedOrExpired()
    {
        await using var db = CreateContext();
        SeedSession(db, DateOnly.FromDateTime(TimeZoneHelper.UtcNow).AddYears(-16), managedByParent: false);
        var candidateStart = TimeZoneHelper.UtcNow.AddHours(2);
        var reservedSession = AddConflictSession(
            db,
            2,
            "tutor-1",
            "another-student",
            TimeZoneHelper.UtcNow.AddDays(-1),
            TimeZoneHelper.UtcNow.AddDays(-1).AddHours(1));
        db.ClassSessionScheduleChanges.Add(new ClassSessionScheduleChange
        {
            Classsessionid = 2,
            ClassSession = reservedSession,
            Originalscheduledstart = reservedSession.Scheduledstart,
            Originalscheduledend = reservedSession.Scheduledend,
            Tutoruserid = "tutor-1",
            Learnerapproveruserid = "another-student-user",
            Learnerapproverrole = UserRole.Student,
            Requestedat = candidateStart.AddMinutes(-1),
            Approvedat = candidateStart,
            Expiresat = candidateStart.AddMinutes(30),
            Status = ScheduleChangeStatus.Approved,
            Createdat = candidateStart.AddMinutes(-1),
            Updatedat = candidateStart
        });
        await db.SaveChangesAsync();

        var conflict = await ClassSessionScheduleConflictGuard.FindAsync(
            db,
            1,
            "tutor-1",
            "student-profile-1",
            candidateStart.AddMinutes(10),
            candidateStart.AddMinutes(40));

        Assert.NotNull(conflict);
        Assert.Equal(2, conflict.ClassSessionId);
        Assert.Equal("tutor", conflict.ConflictingParty);
    }

    private static ClassSession AddConflictSession(
        AgoraDbContext db,
        int id,
        string tutorId,
        string studentId,
        DateTime start,
        DateTime end)
    {
        var session = new ClassSession
        {
            Classsessionid = id,
            Tutorid = tutorId,
            Studentid = studentId,
            Scheduledstart = start,
            Scheduledend = end,
            Status = ClassSessionStatus.Scheduled
        };
        db.ClassSessions.Add(session);
        return session;
    }
    private static ClassSessionScheduleChangeService CreateService(AgoraDbContext db)
        => new(db, null!, NullLogger<ClassSessionScheduleChangeService>.Instance);

    private static AgoraDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AgoraDbContext>()
            .UseInMemoryDatabase($"schedule-change-{Guid.NewGuid()}")
            .Options;
        return new ScheduleChangeTestDbContext(options);
    }

    private static void SeedSession(
        AgoraDbContext db,
        DateOnly birthdate,
        bool managedByParent = true,
        bool isContinuation = false)
    {
        var tutorUser = NewUser("tutor-1", UserRole.Tutor, "Gia sư");
        var parentUser = NewUser("parent-1", UserRole.Parent, "Phụ huynh");
        var studentUser = NewUser("student-user-1", UserRole.Student, "Học sinh");
        studentUser.Birthdate = birthdate;
        var adminUser = NewUser("admin-1", UserRole.Admin, "Quản trị viên");
        var tutor = new Tutorprofile { Tutorid = tutorUser.Userid, Tutor = tutorUser };
        var student = new Studentprofile
        {
            Studentid = "student-profile-1",
            Parentid = managedByParent ? parentUser.Userid : null,
            Parent = managedByParent ? parentUser : null,
            Linkeduserid = studentUser.Userid,
            Linkeduser = studentUser,
            Fullname = studentUser.Fullname,
            Birthdate = birthdate
        };
        var booking = new Booking
        {
            Bookingid = 1,
            Parentid = managedByParent ? parentUser.Userid : null,
            Parent = managedByParent ? parentUser : null,
            Studentid = student.Studentid,
            Student = student,
            Tutorid = tutor.Tutorid,
            Tutor = tutor
        };
        var session = new ClassSession
        {
            Classsessionid = 1,
            Bookingid = booking.Bookingid,
            Booking = booking,
            Tutorid = tutor.Tutorid,
            Tutor = tutor,
            Studentid = student.Studentid,
            Student = student,
            Scheduledstart = TimeZoneHelper.UtcNow.AddHours(-3),
            Scheduledend = TimeZoneHelper.UtcNow.AddHours(-2),
            Status = ClassSessionStatus.Scheduled,
            Iscontinuation = isContinuation
        };
        db.Users.AddRange(tutorUser, parentUser, studentUser, adminUser);
        db.Tutorprofiles.Add(tutor);
        db.Studentprofiles.Add(student);
        db.Bookings.Add(booking);
        db.ClassSessions.Add(session);
    }

    private sealed class ScheduleChangeTestDbContext(DbContextOptions<AgoraDbContext> options)
        : AgoraDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<QuestionBank>().Ignore(x => x.Embedding);
            modelBuilder.Entity<TutoraKbChunk>().Ignore(x => x.Embedding);
        }
    }

    private static User NewUser(string id, string role, string name) => new()
    {
        Userid = id,
        Username = id,
        Password = "test",
        Email = $"{id}@test.local",
        Fullname = name,
        Primaryrole = role
    };
}
