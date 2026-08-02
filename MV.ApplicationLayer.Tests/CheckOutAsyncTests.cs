using Microsoft.Extensions.Logging.Abstractions;
using MV.ApplicationLayer.Services;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.Entities;
using MV.DomainLayer.Exceptions;
using MV.InfrastructureLayer.DBContext;
using Xunit;

namespace MV.ApplicationLayer.Tests;

// Maps to Excel sheet "CheckOutAsync" (Code_26, ClassSessionService.CheckOutAsync).
public class CheckOutAsyncTests
{
    private const string TutorId = "tutor-1";

    [Fact]
    public async Task UnknownSession_ThrowsClassSessionNotFound()
    {
        var ctx = CreateService();

        await Assert.ThrowsAsync<ClassSessionException>(() => ctx.Service.CheckOutAsync(999, TutorId, new CheckOutRequest()));
    }

    [Fact]
    public async Task SessionNotInProgress_ThrowsInvalidClassSessionStatus()
    {
        var ctx = CreateService();
        ctx.Db.ClassSessions.Add(NewSession(1, ClassSessionStatus.Scheduled, checkedIn: false));
        await ctx.Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ClassSessionException>(() => ctx.Service.CheckOutAsync(1, TutorId, new CheckOutRequest()));
        Assert.Equal(ClassSessionErrorCodes.InvalidClassSessionStatus, ex.ErrorCode);
    }

    [Fact]
    public async Task NotCheckedIn_ThrowsNotCheckedIn()
    {
        var ctx = CreateService();
        ctx.Db.ClassSessions.Add(NewSession(2, ClassSessionStatus.InProgress, checkedIn: false));
        await ctx.Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ClassSessionException>(() => ctx.Service.CheckOutAsync(2, TutorId, new CheckOutRequest()));
        Assert.Equal(ClassSessionErrorCodes.NotCheckedIn, ex.ErrorCode);
    }

    [Fact]
    public async Task ValidCheckOut_SetsCheckoutTimeAndRealEnd()
    {
        var ctx = CreateService();
        ctx.Db.ClassSessions.Add(NewSession(3, ClassSessionStatus.InProgress, checkedIn: true));
        await ctx.Db.SaveChangesAsync();

        await ctx.Service.CheckOutAsync(3, TutorId, new CheckOutRequest());

        var updated = ctx.Db.ClassSessions.Single(s => s.Classsessionid == 3);
        Assert.NotNull(updated.Checkouttime);
        Assert.NotNull(updated.Realend);
    }

    private static ClassSession NewSession(int id, string status, bool checkedIn) => new()
    {
        Classsessionid = id,
        Tutorid = TutorId,
        Status = status,
        Checkintime = checkedIn ? DateTime.UtcNow.AddMinutes(-10) : null,
        Scheduledstart = DateTime.UtcNow.AddMinutes(-15),
        Scheduledend = DateTime.UtcNow.AddMinutes(45)
    };

    private static ServiceContext CreateService()
    {
        var db = TestSupport.CreateInMemoryContext("check-out");
        var service = new ClassSessionService(
            null!, null!, null!,
            db,
            null!, null!, null!, null!,
            null!,
            new FakeCloudRecordingService(),
            null!, null!, null!, null!,
            NullLogger<ClassSessionService>.Instance);
        return new ServiceContext(service, db);
    }

    private sealed record ServiceContext(ClassSessionService Service, AgoraDbContext Db);
}
