using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MV.ApplicationLayer.Services;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.Entities;
using MV.DomainLayer.Exceptions;
using MV.InfrastructureLayer.DBContext;
using Xunit;

namespace MV.ApplicationLayer.Tests;

// Maps to Excel sheet "CreateDisputeAsync" (Code_28, ParentService.CreateDisputeAsync).
// UTCID for the success path and for DisputeAlreadyExists are not covered here: both only
// execute once the method enters its Serializable transaction, which locks rows via
// FromSqlRaw(...FOR UPDATE) — Postgres-only syntax that neither the EF Core InMemory
// provider nor SQLite can run. Those two need a real Postgres (integration test), not a unit test.
public class CreateDisputeAsyncTests
{
    [Fact]
    public async Task StudentManagedByParent_CannotCreateDisputeThemselves()
    {
        await using var db = CreateContext();
        db.Studentprofiles.Add(new Studentprofile
        {
            Studentid = "student-profile-1",
            Parentid = "parent-1",
            Linkeduserid = "student-user-1",
            Fullname = "Học sinh được quản lý",
            Createdat = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        var service = CreateService(db);
        var request = new CreateDisputeRequest { DisputeType = DisputeTypes.Quality, Reason = "Buổi học không đạt chất lượng" };

        var ex = await Assert.ThrowsAsync<ClassSessionException>(
            () => service.CreateDisputeAsync(1, "student-user-1", UserRole.Student, request));

        Assert.Equal(BookingErrorCodes.StudentManagedByParent, ex.ErrorCode);
        Assert.Equal(403, ex.HttpStatus);
    }

    [Fact]
    public async Task InvalidDisputeType_ThrowsArgumentException()
    {
        await using var db = CreateContext();
        var service = CreateService(db);
        var request = new CreateDisputeRequest { DisputeType = "not_a_real_type", Reason = "Lý do bất kỳ" };

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.CreateDisputeAsync(1, "parent-1", UserRole.Parent, request));
    }

    [Fact]
    public async Task ClassSessionNotYetOccurred_ThrowsInvalidClassSessionStatus()
    {
        await using var db = CreateContext();
        SeedSession(db, sessionStatus: ClassSessionStatus.Scheduled, bookingStatus: BookingStatus.DepositPaid);
        await db.SaveChangesAsync();
        var service = CreateService(db);
        var request = new CreateDisputeRequest { DisputeType = DisputeTypes.Quality, Reason = "Buổi học chưa diễn ra" };

        var ex = await Assert.ThrowsAsync<ClassSessionException>(
            () => service.CreateDisputeAsync(1, "parent-1", UserRole.Parent, request));

        Assert.Equal(ClassSessionErrorCodes.InvalidClassSessionStatus, ex.ErrorCode);
        Assert.Equal(400, ex.HttpStatus);
    }

    [Fact]
    public async Task BookingAlreadyTerminal_ThrowsInvalidClassSessionStatus()
    {
        await using var db = CreateContext();
        SeedSession(db, sessionStatus: ClassSessionStatus.PendingConfirmation, bookingStatus: BookingStatus.Completed);
        await db.SaveChangesAsync();
        var service = CreateService(db);
        var request = new CreateDisputeRequest { DisputeType = DisputeTypes.Quality, Reason = "Booking đã kết thúc" };

        var ex = await Assert.ThrowsAsync<ClassSessionException>(
            () => service.CreateDisputeAsync(1, "parent-1", UserRole.Parent, request));

        Assert.Equal(ClassSessionErrorCodes.InvalidClassSessionStatus, ex.ErrorCode);
        Assert.Equal(400, ex.HttpStatus);
    }

    [Fact]
    public async Task SessionNotOwnedByCaller_ThrowsClassSessionNotFound()
    {
        await using var db = CreateContext();
        SeedSession(db, sessionStatus: ClassSessionStatus.PendingConfirmation, bookingStatus: BookingStatus.DepositPaid,
            studentId: "other-student", parentId: "other-parent");
        await db.SaveChangesAsync();
        var service = CreateService(db);
        var request = new CreateDisputeRequest { DisputeType = DisputeTypes.Quality, Reason = "Không liên quan tới caller" };

        var ex = await Assert.ThrowsAsync<ClassSessionException>(
            () => service.CreateDisputeAsync(1, "parent-1", UserRole.Parent, request));

        Assert.Equal(ClassSessionErrorCodes.ClassSessionNotFound, ex.ErrorCode);
        Assert.Equal(404, ex.HttpStatus);
    }

    private static void SeedSession(
        AgoraDbContext db,
        string sessionStatus,
        string bookingStatus,
        string studentId = "student-1",
        string parentId = "parent-1")
    {
        db.Studentprofiles.Add(new Studentprofile
        {
            Studentid = studentId,
            Parentid = parentId,
            Fullname = "Học sinh",
            Createdat = DateTime.UtcNow
        });
        db.Bookings.Add(new Booking
        {
            Bookingid = 1,
            Studentid = studentId,
            Parentid = parentId,
            Status = bookingStatus
        });
        db.ClassSessions.Add(new ClassSession
        {
            Classsessionid = 1,
            Bookingid = 1,
            Studentid = studentId,
            Scheduledstart = DateTime.UtcNow.AddHours(-2),
            Scheduledend = DateTime.UtcNow.AddHours(-1),
            Status = sessionStatus
        });
    }

    private static ParentService CreateService(AgoraDbContext db) =>
        new(db, null!, null!, null!, null!, NullLogger<ParentService>.Instance);

    private static AgoraDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AgoraDbContext>()
            .UseInMemoryDatabase($"create-dispute-{Guid.NewGuid()}")
            .Options;
        return new CreateDisputeTestDbContext(options);
    }

    private sealed class CreateDisputeTestDbContext(DbContextOptions<AgoraDbContext> options)
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
