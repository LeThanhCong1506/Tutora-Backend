using Microsoft.Extensions.Logging.Abstractions;
using MV.ApplicationLayer.Services;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.Entities;
using MV.DomainLayer.Exceptions;
using MV.InfrastructureLayer.DBContext;
using Xunit;

namespace MV.ApplicationLayer.Tests;

// Maps to Excel sheet "ProcessNoShowActionAsync" (Code_40, ClassSessionService.ProcessNoShowActionAsync).
// The remedy branches (FreeSession/Makeup/ChangeTutor) run inside a transaction that reaches
// wallet/escrow operations elsewhere in the codebase using FromSqlRaw row locks - not exercised
// here. Covered are the pre-checks that run before the transaction opens.
public class ProcessNoShowActionAsyncTests
{
    private const string ParentId = "parent-1";
    private const string StudentProfileId = "student-profile-1";

    [Fact]
    public async Task InvalidActionType_ThrowsInvalidNoShowAction()
    {
        var ctx = CreateService();

        var ex = await Assert.ThrowsAsync<ClassSessionException>(
            () => ctx.Service.ProcessNoShowActionAsync(1, ParentId, UserRole.Parent, new NoShowActionRequest { ActionType = "not_a_real_action" }));
        Assert.Equal(ClassSessionErrorCodes.InvalidNoShowAction, ex.ErrorCode);
    }

    [Fact]
    public async Task SessionNotOwnedByCaller_ThrowsClassSessionNotFound()
    {
        var ctx = CreateService();

        await Assert.ThrowsAsync<ClassSessionException>(
            () => ctx.Service.ProcessNoShowActionAsync(999, ParentId, UserRole.Parent, new NoShowActionRequest { ActionType = NoShowActionTypes.FreeSession }));
    }

    [Fact]
    public async Task SessionNotInNoShowStatus_ThrowsInvalidClassSessionStatus()
    {
        var ctx = CreateService();
        ctx.Db.Studentprofiles.Add(new Studentprofile { Studentid = StudentProfileId, Parentid = ParentId, Fullname = "Học sinh", Createdat = DateTime.UtcNow });
        ctx.Db.ClassSessions.Add(new ClassSession
        {
            Classsessionid = 1,
            Studentid = StudentProfileId,
            Status = ClassSessionStatus.Scheduled,
            Scheduledstart = DateTime.UtcNow.AddMinutes(-20),
            Scheduledend = DateTime.UtcNow.AddMinutes(40)
        });
        await ctx.Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ClassSessionException>(
            () => ctx.Service.ProcessNoShowActionAsync(1, ParentId, UserRole.Parent, new NoShowActionRequest { ActionType = NoShowActionTypes.FreeSession }));
        Assert.Equal(ClassSessionErrorCodes.InvalidClassSessionStatus, ex.ErrorCode);
    }

    private static ServiceContext CreateService()
    {
        var db = TestSupport.CreateInMemoryContext("process-no-show-action");
        var service = new ClassSessionService(
            null!, null!, null!,
            db,
            null!, null!, null!, null!,
            null!, null!, null!, null!, null!, null!,
            NullLogger<ClassSessionService>.Instance);
        return new ServiceContext(service, db);
    }

    private sealed record ServiceContext(ClassSessionService Service, AgoraDbContext Db);
}
