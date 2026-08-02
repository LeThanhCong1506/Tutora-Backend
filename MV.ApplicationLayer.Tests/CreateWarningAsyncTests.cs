using Microsoft.Extensions.Logging.Abstractions;
using MV.ApplicationLayer.Services;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.Entities;
using MV.InfrastructureLayer.DBContext;
using MV.InfrastructureLayer.Repositories;
using Xunit;

namespace MV.ApplicationLayer.Tests;

// Maps to Excel sheet "CreateWarningAsync" (Code_41, WarningService.CreateWarningAsync).
public class CreateWarningAsyncTests
{
    [Fact]
    public async Task UnknownUser_ThrowsArgumentException()
    {
        var ctx = CreateService();

        await Assert.ThrowsAsync<ArgumentException>(
            () => ctx.Service.CreateWarningAsync("no-such-user", new CreateWarningRequest { WarningLevel = WarningLevel.Low, Reason = "Đi trễ nhiều lần" }, "admin-1"));
    }

    [Fact]
    public async Task SingleLowWarning_CreatesWarningAndNotifiesWithoutSuspension()
    {
        var ctx = CreateService();
        ctx.Db.Users.Add(NewUser("target-1"));
        await ctx.Db.SaveChangesAsync();

        var result = await ctx.Service.CreateWarningAsync("target-1", new CreateWarningRequest { WarningLevel = WarningLevel.Low, Reason = "Đi trễ buổi học" }, "admin-1");

        Assert.Equal(WarningLevel.Low, result.WarningLevel);
        Assert.Single(ctx.Notifications.SentSingle);
        var user = await ctx.Db.Users.FindAsync("target-1");
        Assert.Equal(1, user!.Status);
    }

    [Fact]
    public async Task HighWarning_TriggersImmediateSuspension()
    {
        var ctx = CreateService();
        ctx.Db.Users.Add(NewUser("target-2"));
        await ctx.Db.SaveChangesAsync();

        await ctx.Service.CreateWarningAsync("target-2", new CreateWarningRequest { WarningLevel = WarningLevel.High, Reason = "Vi phạm nghiêm trọng quy định" }, "admin-1");

        var user = await ctx.Db.Users.FindAsync("target-2");
        Assert.Equal(0, user!.Status);
    }

    private static User NewUser(string id) => new()
    {
        Userid = id,
        Password = "hash",
        Fullname = "Người dùng bị cảnh báo",
        Primaryrole = UserRole.Tutor,
        Status = 1,
        Createdat = DateTime.UtcNow
    };

    private static ServiceContext CreateService()
    {
        var db = TestSupport.CreateInMemoryContext("create-warning");
        var notifications = new FakeNotificationService();
        var service = new WarningService(
            new WarningRepository(db),
            new UserRepository(db, new PasswordRepository()),
            db,
            notifications,
            NullLogger<WarningService>.Instance);
        return new ServiceContext(service, db, notifications);
    }

    private sealed record ServiceContext(WarningService Service, AgoraDbContext Db, FakeNotificationService Notifications);
}
