using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
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
    public async Task ExactlySixteen_RequiresStudentAndTutorConfirmation()
    {
        await using var db = CreateContext();
        SeedSession(db, DateOnly.FromDateTime(TimeZoneHelper.UtcNow).AddYears(-16));
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var tutorState = await service.GetOrCreateStateAsync(1, "tutor-1", UserRole.Tutor);
        Assert.True(tutorState.RequiresConfirmation);
        Assert.Equal(UserRole.Student, tutorState.RequiredLearnerRole);

        var afterTutor = await service.RespondAsync(1, "tutor-1", UserRole.Tutor, true);
        Assert.Equal(ScheduleChangeStatus.Pending, afterTutor.Status);
        Assert.NotNull(afterTutor.TutorConfirmedAt);

        var approved = await service.RespondAsync(1, "student-user-1", UserRole.Student, true);
        Assert.Equal(ScheduleChangeStatus.Approved, approved.Status);
        Assert.True(approved.AdmissionAllowed);
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
