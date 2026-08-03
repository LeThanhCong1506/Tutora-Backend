using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MV.ApplicationLayer.Services;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.Entities;
using MV.InfrastructureLayer;
using MV.InfrastructureLayer.DBContext;
using MV.InfrastructureLayer.Repositories;
using Xunit;

namespace MV.ApplicationLayer.Tests;

// Maps to Excel sheet "CreateTutorPackageAsync" (Code_23, TutorService.CreateTutorPackageAsync).
public class CreateTutorPackageAsyncTests
{
    private const string TutorId = "tutor-1";

    [Fact]
    public async Task MissingTutorProfile_ReturnsNull()
    {
        var ctx = CreateService();

        var result = await ctx.Service.CreateTutorPackageAsync(TutorId, FlexibleRequest());

        Assert.Null(result);
    }

    [Fact]
    public async Task ValidFlexiblePackage_CreatesPackageWithoutSlots()
    {
        var ctx = CreateService();
        await SeedProfileAsync(ctx.Db);

        var result = await ctx.Service.CreateTutorPackageAsync(TutorId, FlexibleRequest());

        Assert.NotNull(result);
        Assert.Equal(Tutorpackage.FlexiblePackageType, result!.PackageType);
        Assert.Empty(result.FixedSlots);
    }

    [Fact]
    public async Task BlankName_ThrowsArgumentException()
    {
        var ctx = CreateService();
        await SeedProfileAsync(ctx.Db);
        var request = FlexibleRequest();
        request.Name = "   ";

        await Assert.ThrowsAsync<ArgumentException>(() => ctx.Service.CreateTutorPackageAsync(TutorId, request));
    }

    [Fact]
    public async Task InvalidPackageType_ThrowsArgumentException()
    {
        var ctx = CreateService();
        await SeedProfileAsync(ctx.Db);

        await Assert.ThrowsAsync<ArgumentException>(() => ctx.Service.CreateTutorPackageAsync(TutorId, new CreateTutorPackageRequest { Name = "Gói lạ", PackageType = 99 }));
    }

    [Fact]
    public async Task FixedPackageWithoutSlots_ThrowsArgumentException()
    {
        var ctx = CreateService();
        await SeedProfileAsync(ctx.Db);

        await Assert.ThrowsAsync<ArgumentException>(() => ctx.Service.CreateTutorPackageAsync(TutorId, new CreateTutorPackageRequest { Name = "Gói cố định", PackageType = Tutorpackage.FixedPackageType }));
    }

    [Fact]
    public async Task FlexiblePackageWithSlots_ThrowsArgumentException()
    {
        var ctx = CreateService();
        await SeedProfileAsync(ctx.Db);
        var request = FlexibleRequest();
        request.FixedSlots.Add(new TutorPackageFixedSlotRequest { DayOfWeek = 1, StartTime = "09:00", EndTime = "10:00" });

        await Assert.ThrowsAsync<ArgumentException>(() => ctx.Service.CreateTutorPackageAsync(TutorId, request));
    }

    [Fact]
    public async Task FixedSlotOverlapsCommittedSession_ThrowsInvalidOperationException()
    {
        var ctx = CreateService();
        await SeedProfileAsync(ctx.Db);
        var nextMonday = NextDayOfWeek(DayOfWeek.Monday);
        ctx.Db.ClassSessions.Add(new ClassSession
        {
            Classsessionid = 1,
            Tutorid = TutorId,
            Status = MV.DomainLayer.Constants.ClassSessionStatus.Scheduled,
            Scheduledstart = nextMonday.AddHours(9),
            Scheduledend = nextMonday.AddHours(10)
        });
        await ctx.Db.SaveChangesAsync();

        var request = new CreateTutorPackageRequest
        {
            Name = "Gói cố định",
            PackageType = Tutorpackage.FixedPackageType,
            FixedSlots = new List<TutorPackageFixedSlotRequest>
            {
                new() { DayOfWeek = 1, StartTime = "09:00", EndTime = "10:00" }
            }
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => ctx.Service.CreateTutorPackageAsync(TutorId, request));
    }

    [Fact]
    public async Task ValidFixedPackage_CreatesPackageWithSlots()
    {
        var ctx = CreateService();
        await SeedProfileAsync(ctx.Db);

        var request = new CreateTutorPackageRequest
        {
            Name = "Gói cố định",
            PackageType = Tutorpackage.FixedPackageType,
            FixedSlots = new List<TutorPackageFixedSlotRequest>
            {
                new() { DayOfWeek = 1, StartTime = "09:00", EndTime = "10:00" }
            }
        };

        var result = await ctx.Service.CreateTutorPackageAsync(TutorId, request);

        Assert.NotNull(result);
        var stored = await ctx.Db.Tutorpackages.Include(p => p.Tutorpackagefixedslots).AsNoTracking().SingleAsync(p => p.Tutorid == TutorId);
        Assert.Single(stored.Tutorpackagefixedslots);
    }

    private static CreateTutorPackageRequest FlexibleRequest() => new() { Name = "Gói linh hoạt", PackageType = Tutorpackage.FlexiblePackageType };

    private static DateTime NextDayOfWeek(DayOfWeek target)
    {
        var start = DateTime.UtcNow.Date.AddDays(1);
        while (start.DayOfWeek != target) start = start.AddDays(1);
        return start;
    }

    private static async Task SeedProfileAsync(AgoraDbContext db)
    {
        db.Tutorprofiles.Add(new Tutorprofile { Tutorid = TutorId, Createdat = DateTime.UtcNow });
        await db.SaveChangesAsync();
    }

    private static ServiceContext CreateService()
    {
        var db = TestSupport.CreateInMemoryContext("create-tutor-package");
        var unitOfWork = new UnitOfWork(db, new PasswordRepository(), NullLogger<UnitOfWork>.Instance);
        var service = new TutorService(
            unitOfWork,
            new FakeFileStorageService(),
            null!,
            new FakeNotificationService(),
            NullLogger<TutorService>.Instance,
            null!,
            null!,
            db,
            null!,
            new FakeTutorEmbedQueue());
        return new ServiceContext(service, db);
    }

    private sealed record ServiceContext(TutorService Service, AgoraDbContext Db);
}
