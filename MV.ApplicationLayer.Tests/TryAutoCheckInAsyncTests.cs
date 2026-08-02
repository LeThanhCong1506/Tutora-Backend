using Microsoft.Extensions.Logging.Abstractions;
using MV.ApplicationLayer.Services;
using MV.DomainLayer.Constants;
using MV.DomainLayer.Entities;
using MV.InfrastructureLayer.DBContext;
using Xunit;

namespace MV.ApplicationLayer.Tests;

// Maps to Excel sheet "TryAutoCheckInAsync" (Code_14, ClassSessionService.TryAutoCheckInAsync).
// The actual check-in transition uses EF Core's ExecuteUpdateAsync, unsupported by the InMemory
// provider - covered here are the branches that return before reaching it (not found, only one
// side present, payment-blocked, already checked in). The real check-in transition itself needs
// an integration test against a real Postgres/SQLite database.
public class TryAutoCheckInAsyncTests
{
    private const string TutorId = "tutor-1";
    private const string StudentUserId = "student-user-1";
    private const string ParentId = "parent-1";

    [Fact]
    public async Task UnknownSession_ReturnsAllFalseStatus()
    {
        var ctx = CreateService();

        var result = await ctx.Service.TryAutoCheckInAsync(999);

        Assert.False(result.TutorPresent);
        Assert.False(result.StudentPresent);
        Assert.False(result.IsCheckedIn);
    }

    [Fact]
    public async Task OnlyTutorPresent_DoesNotCheckIn()
    {
        var ctx = CreateService();
        SeedScheduledSession(ctx.Db, sessionId: 1, bookingStatus: BookingStatus.DepositPaid);
        await ctx.Db.SaveChangesAsync();
        ctx.Presence.SetPresent(1, TutorId);

        var result = await ctx.Service.TryAutoCheckInAsync(1);

        Assert.True(result.TutorPresent);
        Assert.False(result.StudentPresent);
        Assert.False(result.IsCheckedIn);
    }

    [Fact]
    public async Task BothPresentButNextSessionBlockedByRemainingPayment_ReturnsBlockedByPayment()
    {
        var ctx = CreateService();
        SeedScheduledSession(ctx.Db, sessionId: 2, bookingStatus: BookingStatus.PendingRemainingPayment, bookingId: 10);
        ctx.Db.ClassSessions.Add(new ClassSession
        {
            Classsessionid = 3,
            Bookingid = 10,
            Tutorid = TutorId,
            Status = ClassSessionStatus.Completed,
            Scheduledstart = DateTime.UtcNow.AddDays(-1),
            Scheduledend = DateTime.UtcNow.AddDays(-1).AddHours(1)
        });
        await ctx.Db.SaveChangesAsync();
        ctx.Presence.SetPresent(2, TutorId);
        ctx.Presence.SetPresent(2, StudentUserId);

        var result = await ctx.Service.TryAutoCheckInAsync(2);

        Assert.True(result.BlockedByPayment);
        Assert.False(result.IsCheckedIn);
    }

    [Fact]
    public async Task AlreadyInProgress_ReflectsExistingCheckInWithoutReprocessing()
    {
        var ctx = CreateService();
        var session = new ClassSession
        {
            Classsessionid = 4,
            Bookingid = 20,
            Tutorid = TutorId,
            Studentid = "student-profile-1",
            Status = ClassSessionStatus.InProgress,
            Checkintime = DateTime.UtcNow.AddMinutes(-5),
            Scheduledstart = DateTime.UtcNow.AddMinutes(-10),
            Scheduledend = DateTime.UtcNow.AddMinutes(50)
        };
        ctx.Db.Bookings.Add(new Booking { Bookingid = 20, Tutorid = TutorId, Status = BookingStatus.DepositPaid });
        ctx.Db.ClassSessions.Add(session);
        await ctx.Db.SaveChangesAsync();

        var result = await ctx.Service.TryAutoCheckInAsync(4);

        Assert.True(result.IsCheckedIn);
    }

    private static void SeedScheduledSession(AgoraDbContext db, int sessionId, string bookingStatus, int bookingId = 100)
    {
        db.Studentprofiles.Add(new Studentprofile { Studentid = "student-profile-1", Linkeduserid = StudentUserId, Fullname = "Học sinh", Createdat = DateTime.UtcNow });
        db.Bookings.Add(new Booking { Bookingid = bookingId, Tutorid = TutorId, Parentid = ParentId, Studentid = "student-profile-1", Status = bookingStatus });
        db.ClassSessions.Add(new ClassSession
        {
            Classsessionid = sessionId,
            Bookingid = bookingId,
            Tutorid = TutorId,
            Studentid = "student-profile-1",
            Status = ClassSessionStatus.Scheduled,
            Scheduledstart = DateTime.UtcNow.AddMinutes(-5),
            Scheduledend = DateTime.UtcNow.AddMinutes(55)
        });
    }

    private static ServiceContext CreateService()
    {
        var db = TestSupport.CreateInMemoryContext("try-auto-checkin");
        var presence = new FakeSessionPresenceService();
        var service = new ClassSessionService(
            null!, null!, null!,
            db,
            null!, null!, null!, null!,
            presence,
            null!, null!, null!, null!, null!,
            NullLogger<ClassSessionService>.Instance);
        return new ServiceContext(service, db, presence);
    }

    private sealed record ServiceContext(ClassSessionService Service, AgoraDbContext Db, FakeSessionPresenceService Presence);
}
