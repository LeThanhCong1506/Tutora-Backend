using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MV.ApplicationLayer.Services;
using MV.DomainLayer.Constants;
using MV.DomainLayer.Entities;
using MV.DomainLayer.Helpers;
using MV.InfrastructureLayer.DBContext;
using MV.InfrastructureLayer.Repositories;
using Xunit;

namespace MV.ApplicationLayer.Tests;

// Maps to Excel sheet "SuspendUserAsync" (Code_30, WarningService.CheckAndApplySuspensionAsync).
public class SuspendUserAsyncTests
{
    private const string UserId = "target-1";

    [Fact]
    public async Task AlreadyActivelySuspended_ReturnsFalseAndSkips()
    {
        var ctx = CreateService();
        ctx.Db.Users.Add(NewUser());
        ctx.Db.Profilesuspensions.Add(NewSuspension(SuspensionType.Temporary, isActive: true, startedDaysAgo: 1));
        await ctx.Db.SaveChangesAsync();

        var result = await ctx.Service.CheckAndApplySuspensionAsync(UserId);

        Assert.False(result);
    }

    [Fact]
    public async Task SingleHighWarning_TriggersTemporarySuspension()
    {
        var ctx = CreateService();
        ctx.Db.Users.Add(NewUser());
        ctx.Db.Userwarnings.Add(NewWarning(WarningLevel.High, daysAgo: 1));
        await ctx.Db.SaveChangesAsync();

        var result = await ctx.Service.CheckAndApplySuspensionAsync(UserId);

        Assert.True(result);
        var suspension = await ctx.Db.Profilesuspensions.SingleAsync(s => s.Userid == UserId);
        Assert.Equal(SuspensionType.Temporary, suspension.Suspensiontype);
        Assert.NotNull(suspension.Enddate);
    }

    [Fact]
    public async Task ThreeLowMediumWarningsWithin30Days_TriggersTemporarySuspension()
    {
        var ctx = CreateService();
        ctx.Db.Users.Add(NewUser());
        ctx.Db.Userwarnings.AddRange(
            NewWarning(WarningLevel.Low, daysAgo: 1),
            NewWarning(WarningLevel.Medium, daysAgo: 5),
            NewWarning(WarningLevel.Low, daysAgo: 10));
        await ctx.Db.SaveChangesAsync();

        var result = await ctx.Service.CheckAndApplySuspensionAsync(UserId);

        Assert.True(result);
        var suspension = await ctx.Db.Profilesuspensions.SingleAsync(s => s.Userid == UserId);
        Assert.Equal(SuspensionType.Temporary, suspension.Suspensiontype);
    }

    [Fact]
    public async Task TwoLowMediumWarnings_BelowThreshold_DoesNotSuspend()
    {
        var ctx = CreateService();
        ctx.Db.Users.Add(NewUser());
        ctx.Db.Userwarnings.AddRange(
            NewWarning(WarningLevel.Low, daysAgo: 1),
            NewWarning(WarningLevel.Medium, daysAgo: 5));
        await ctx.Db.SaveChangesAsync();

        var result = await ctx.Service.CheckAndApplySuspensionAsync(UserId);

        Assert.False(result);
        Assert.Empty(ctx.Db.Profilesuspensions.Where(s => s.Userid == UserId));
    }

    [Fact]
    public async Task AlreadyAutoSuspendedOnceWithin30Days_EscalatesToPermanent()
    {
        var ctx = CreateService();
        ctx.Db.Users.Add(NewUser());
        // Prior temporary suspension (now inactive - e.g. already expired/lifted) counts toward escalation.
        ctx.Db.Profilesuspensions.Add(NewSuspension(SuspensionType.Temporary, isActive: false, startedDaysAgo: 10));
        ctx.Db.Userwarnings.Add(NewWarning(WarningLevel.High, daysAgo: 1));
        await ctx.Db.SaveChangesAsync();

        var result = await ctx.Service.CheckAndApplySuspensionAsync(UserId);

        Assert.True(result);
        var latest = await ctx.Db.Profilesuspensions
            .Where(s => s.Userid == UserId)
            .OrderByDescending(s => s.Startdate)
            .FirstAsync();
        Assert.Equal(SuspensionType.Permanent, latest.Suspensiontype);
        Assert.Null(latest.Enddate);
    }

    private static User NewUser() => new()
    {
        Userid = UserId,
        Password = "hash",
        Fullname = "Người dùng",
        Primaryrole = UserRole.Tutor,
        Status = 1,
        Createdat = DateTime.UtcNow
    };

    private static Userwarning NewWarning(int level, int daysAgo) => new()
    {
        Userid = UserId,
        Warninglevel = level,
        Reason = "Vi phạm quy định nền tảng",
        Issuedby = "admin-1",
        Createdat = TimeZoneHelper.UtcNow.AddDays(-daysAgo)
    };

    private static Profilesuspension NewSuspension(string type, bool isActive, int startedDaysAgo) => new()
    {
        Userid = UserId,
        Suspensiontype = type,
        Reason = "Auto-suspended",
        Startdate = TimeZoneHelper.UtcNow.AddDays(-startedDaysAgo),
        Enddate = isActive ? TimeZoneHelper.UtcNow.AddDays(7 - startedDaysAgo) : TimeZoneHelper.UtcNow.AddDays(-startedDaysAgo + 1),
        Isactive = isActive
    };

    private static ServiceContext CreateService()
    {
        var db = TestSupport.CreateInMemoryContext("suspend-user");
        var service = new WarningService(
            new WarningRepository(db),
            new UserRepository(db, new PasswordRepository()),
            db,
            new FakeNotificationService(),
            NullLogger<WarningService>.Instance);
        return new ServiceContext(service, db);
    }

    private sealed record ServiceContext(WarningService Service, AgoraDbContext Db);
}
