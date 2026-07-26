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
    [Fact]
    public async Task ExactlySixteenWithoutParent_RequiresStudentAndTutorConfirmation()
    {
        await using var db = CreateContext();
        SeedSession(db, DateOnly.FromDateTime(TimeZoneHelper.UtcNow).AddYears(-16), managedByParent: false);
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var tutorState = await service.GetOrCreateStateAsync(1, "tutor-1", UserRole.Tutor);
        Assert.True(tutorState.RequiresConfirmation);
        Assert.Equal(UserRole.Student, tutorState.RequiredLearnerRole);
        Assert.Equal("tutor-1", tutorState.TutorUserId);
        Assert.Equal("student-user-1", tutorState.LearnerApproverUserId);

        var afterTutor = await service.RespondAsync(1, "tutor-1", UserRole.Tutor, true);
        Assert.Equal(ScheduleChangeStatus.Pending, afterTutor.Status);
        Assert.NotNull(afterTutor.TutorConfirmedAt);

        var approved = await service.RespondAsync(1, "student-user-1", UserRole.Student, true);
        Assert.Equal(ScheduleChangeStatus.Approved, approved.Status);
        Assert.True(approved.AdmissionAllowed);
    }


    [Fact]
    public async Task ParentManagedStudent_RequiresParentEvenWhenStudentIsOverSixteen()
    {
        await using var db = CreateContext();
        SeedSession(db, DateOnly.FromDateTime(TimeZoneHelper.UtcNow).AddYears(-17));
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var studentState = await service.GetOrCreateStateAsync(1, "student-user-1", UserRole.Student);

        Assert.Equal(UserRole.Parent, studentState.RequiredLearnerRole);
        Assert.Equal("parent-1", studentState.LearnerApproverUserId);
        Assert.False(studentState.CanCurrentUserConfirm);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => service.RespondAsync(1, "student-user-1", UserRole.Student, true));
    }

    [Fact]
    public async Task ExistingStudentConsentRequest_IsReplacedWhenStudentIsUnderSixteen()
    {
        await using var db = CreateContext();
        SeedSession(db, DateOnly.FromDateTime(TimeZoneHelper.UtcNow).AddYears(-15));
        await db.SaveChangesAsync();
        var session = await db.ClassSessions.SingleAsync();
        var now = TimeZoneHelper.UtcNow;
        db.ClassSessionScheduleChanges.Add(new ClassSessionScheduleChange
        {
            Classsessionid = session.Classsessionid,
            Originalscheduledstart = session.Scheduledstart,
            Originalscheduledend = session.Scheduledend,
            Tutoruserid = "tutor-1",
            Learnerapproveruserid = "student-user-1",
            Learnerapproverrole = UserRole.Student,
            Requestedat = now,
            Expiresat = now.AddMinutes(30),
            Status = ScheduleChangeStatus.Pending,
            Createdat = now,
            Updatedat = now
        });
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var state = await service.GetOrCreateStateAsync(1, "parent-1", UserRole.Parent);
        var changes = await db.ClassSessionScheduleChanges.OrderBy(x => x.Schedulechangeid).ToListAsync();

        Assert.Equal(UserRole.Parent, state.RequiredLearnerRole);
        Assert.True(state.CanCurrentUserConfirm);
        Assert.Equal(2, changes.Count);
        Assert.Equal(ScheduleChangeStatus.Expired, changes[0].Status);
        Assert.Equal("parent-1", changes[1].Learnerapproveruserid);
    }

    [Fact]
    public async Task Minor_RequiresParentInsteadOfStudent()
    {
        await using var db = CreateContext();
        SeedSession(db, DateOnly.FromDateTime(TimeZoneHelper.UtcNow).AddYears(-15));
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var studentState = await service.GetOrCreateStateAsync(1, "student-user-1", UserRole.Student);
        Assert.Equal(UserRole.Parent, studentState.RequiredLearnerRole);
        Assert.False(studentState.CanCurrentUserConfirm);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => service.RespondAsync(1, "student-user-1", UserRole.Student, true));

        await service.RespondAsync(1, "tutor-1", UserRole.Tutor, true);
        var approved = await service.RespondAsync(1, "parent-1", UserRole.Parent, true);
        Assert.Equal(ScheduleChangeStatus.Approved, approved.Status);
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
    public async Task ParentDetailRead_ReturnsRequestCreatedByTutor()
    {
        await using var db = CreateContext();
        SeedSession(db, DateOnly.FromDateTime(TimeZoneHelper.UtcNow).AddYears(-15));
        await db.SaveChangesAsync();
        var service = CreateService(db);
        await service.GetOrCreateStateAsync(1, "tutor-1", UserRole.Tutor);

        var state = await service.GetExistingStateAsync(1, "parent-1", UserRole.Parent);

        Assert.True(state.RequiresConfirmation);
        Assert.True(state.CanCurrentUserConfirm);
        Assert.Equal(UserRole.Parent, state.RequiredLearnerRole);
        Assert.Equal("parent-1", state.LearnerApproverUserId);
        Assert.Single(await db.ClassSessionScheduleChanges.ToListAsync());
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

    [Fact]
    public async Task TutorConflict_KeepsBothConfirmationsButBlocksAdmission()
    {
        await using var db = CreateContext();
        SeedSession(db, DateOnly.FromDateTime(TimeZoneHelper.UtcNow).AddYears(-16), managedByParent: false);
        AddConflictSession(db, 2, "tutor-1", "another-student", TimeZoneHelper.UtcNow.AddMinutes(10), TimeZoneHelper.UtcNow.AddMinutes(40));
        await db.SaveChangesAsync();
        var service = CreateService(db);

        await service.RespondAsync(1, "tutor-1", UserRole.Tutor, true);
        var approved = await service.RespondAsync(1, "student-user-1", UserRole.Student, true);

        Assert.Equal(ScheduleChangeStatus.Approved, approved.Status);
        Assert.False(approved.AdmissionAllowed);
        Assert.NotNull(approved.ScheduleConflict);
        Assert.Equal("tutor", approved.ScheduleConflict.ConflictingParty);
        db.ChangeTracker.Clear();
        var change = await db.ClassSessionScheduleChanges.SingleAsync();
        Assert.NotNull(change.Tutorconfirmedat);
        Assert.NotNull(change.Learnerconfirmedat);
        Assert.Equal(ScheduleChangeStatus.Approved, change.Status);
    }

    [Fact]
    public async Task StudentConflict_KeepsBothConfirmationsButBlocksAdmission()
    {
        await using var db = CreateContext();
        SeedSession(db, DateOnly.FromDateTime(TimeZoneHelper.UtcNow).AddYears(-16), managedByParent: false);
        AddConflictSession(db, 2, "another-tutor", "student-profile-1", TimeZoneHelper.UtcNow.AddMinutes(10), TimeZoneHelper.UtcNow.AddMinutes(40));
        await db.SaveChangesAsync();
        var service = CreateService(db);

        await service.RespondAsync(1, "tutor-1", UserRole.Tutor, true);
        var approved = await service.RespondAsync(1, "student-user-1", UserRole.Student, true);

        Assert.Equal(ScheduleChangeStatus.Approved, approved.Status);
        Assert.False(approved.AdmissionAllowed);
        Assert.NotNull(approved.ScheduleConflict);
        Assert.Equal("student", approved.ScheduleConflict.ConflictingParty);
    }

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

    private static void SeedSession(AgoraDbContext db, DateOnly birthdate, bool managedByParent = true)
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
            Status = ClassSessionStatus.Scheduled
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
