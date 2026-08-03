using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MV.ApplicationLayer.Services;
using MV.DomainLayer.Constants;
using MV.DomainLayer.Entities;
using MV.DomainLayer.Exceptions;
using MV.InfrastructureLayer.DBContext;
using Xunit;

namespace MV.ApplicationLayer.Tests;

// Maps to Excel sheet "ReportTutorNoShowAsync" (Code_29, ClassSessionService.ReportTutorNoShowAsync).
public class ReportTutorNoShowAsyncTests
{
    private const string ParentId = "parent-1";
    private const string StudentProfileId = "student-profile-1";

    [Fact]
    public async Task SessionNotOwnedByCaller_ThrowsClassSessionNotFound()
    {
        var ctx = CreateService();

        await Assert.ThrowsAsync<ClassSessionException>(
            () => ctx.Service.ReportTutorNoShowAsync(999, ParentId, UserRole.Parent));
    }

    [Fact]
    public async Task StudentManagedByParent_ThrowsForbidden()
    {
        var ctx = CreateService();
        const string studentUserId = "student-user-1";
        ctx.Db.Studentprofiles.Add(new Studentprofile { Studentid = StudentProfileId, Parentid = ParentId, Linkeduserid = studentUserId, Fullname = "Học sinh", Createdat = DateTime.UtcNow });
        ctx.Db.ClassSessions.Add(NewSession(1, ClassSessionStatus.Scheduled));
        await ctx.Db.SaveChangesAsync();

        await Assert.ThrowsAsync<ClassSessionException>(
            () => ctx.Service.ReportTutorNoShowAsync(1, studentUserId, UserRole.Student));
    }

    [Fact]
    public async Task SessionNotScheduled_ThrowsInvalidClassSessionStatus()
    {
        var ctx = CreateService();
        ctx.Db.Studentprofiles.Add(new Studentprofile { Studentid = StudentProfileId, Parentid = ParentId, Fullname = "Học sinh", Createdat = DateTime.UtcNow });
        ctx.Db.ClassSessions.Add(NewSession(2, ClassSessionStatus.Completed));
        await ctx.Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ClassSessionException>(
            () => ctx.Service.ReportTutorNoShowAsync(2, ParentId, UserRole.Parent));
        Assert.Equal(ClassSessionErrorCodes.InvalidClassSessionStatus, ex.ErrorCode);
    }

    [Fact]
    public async Task ValidReport_MarksNoShowAndCreatesDispute()
    {
        var ctx = CreateService();
        ctx.Db.Studentprofiles.Add(new Studentprofile { Studentid = StudentProfileId, Parentid = ParentId, Fullname = "Học sinh", Createdat = DateTime.UtcNow });
        ctx.Db.ClassSessions.Add(NewSession(3, ClassSessionStatus.Scheduled));
        await ctx.Db.SaveChangesAsync();

        await ctx.Service.ReportTutorNoShowAsync(3, ParentId, UserRole.Parent);

        var session = ctx.Db.ClassSessions.Single(s => s.Classsessionid == 3);
        Assert.Equal(ClassSessionStatus.NoShow, session.Status);
        Assert.Single(ctx.Db.Disputes.Where(d => d.Classsessionid == 3));
    }

    [Fact]
    public async Task ReportedLessThanFifteenMinutesLate_IsStillAccepted()
    {
        // The old "must be >=15 minutes late" gate was removed by product decision - the reported
        // time is advisory context for admins now, so an early report must NOT be rejected.
        var ctx = CreateService();
        ctx.Db.Studentprofiles.Add(new Studentprofile { Studentid = StudentProfileId, Parentid = ParentId, Fullname = "Học sinh", Createdat = DateTime.UtcNow });
        ctx.Db.ClassSessions.Add(new ClassSession
        {
            Classsessionid = 4,
            Bookingid = 100,
            Studentid = StudentProfileId,
            Tutorid = "tutor-1",
            Status = ClassSessionStatus.Scheduled,
            Scheduledstart = DateTime.UtcNow.AddMinutes(-2),
            Scheduledend = DateTime.UtcNow.AddMinutes(58)
        });
        await ctx.Db.SaveChangesAsync();

        await ctx.Service.ReportTutorNoShowAsync(4, ParentId, UserRole.Parent);

        var session = ctx.Db.ClassSessions.Single(s => s.Classsessionid == 4);
        Assert.Equal(ClassSessionStatus.NoShow, session.Status);
    }

    private static ClassSession NewSession(int id, string status) => new()
    {
        Classsessionid = id,
        Bookingid = 100,
        Studentid = StudentProfileId,
        Tutorid = "tutor-1",
        Status = status,
        Scheduledstart = DateTime.UtcNow.AddMinutes(-20),
        Scheduledend = DateTime.UtcNow.AddMinutes(40)
    };

    private static ServiceContext CreateService()
    {
        var db = TestSupport.CreateInMemoryContext("report-no-show");
        var service = new ClassSessionService(
            null!, null!, null!,
            db,
            null!, null!, null!,
            new FakeFileStorageService(),
            null!, null!, null!, null!, null!, null!,
            NullLogger<ClassSessionService>.Instance);
        return new ServiceContext(service, db);
    }

    private sealed record ServiceContext(ClassSessionService Service, AgoraDbContext Db);
}
