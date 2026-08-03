using Microsoft.EntityFrameworkCore;
using MV.ApplicationLayer.Services;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.Entities;
using MV.InfrastructureLayer;
using MV.InfrastructureLayer.DBContext;
using MV.InfrastructureLayer.Repositories;
using Xunit;

namespace MV.ApplicationLayer.Tests;

// Maps to Excel sheet "ApproveTutorProfileAsync" (Code_16, UserService.ApproveTutorProfileAsync).
public class ApproveTutorProfileAsyncTests
{
    private const string AdminId = "admin-1";

    [Fact]
    public async Task MissingProfile_ThrowsKeyNotFoundException()
    {
        var ctx = CreateService();

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => ctx.Service.ApproveTutorProfileAsync("no-such-tutor", new ApproveTutorRequest { IsApproved = true }, AdminId));
    }

    [Fact]
    public async Task Approve_ActivatesProfileAndClearsRejectionNote()
    {
        var ctx = CreateService();
        ctx.Db.Users.Add(NewUser("tutor-1"));
        ctx.Db.Tutorprofiles.Add(NewProfile("tutor-1", TutorProfileStatus.Draft, rejectionNote: "hồ sơ thiếu ảnh"));
        await ctx.Db.SaveChangesAsync();

        var result = await ctx.Service.ApproveTutorProfileAsync("tutor-1", new ApproveTutorRequest { IsApproved = true }, AdminId);

        Assert.Equal("Approved", result.IsApproved);
        var profile = await ctx.Db.Tutorprofiles.AsNoTracking().SingleAsync(p => p.Tutorid == "tutor-1");
        Assert.Equal(TutorProfileStatus.Active, profile.Profilestatus);
        Assert.True(profile.Ispublic);
        Assert.Null(profile.Rejectionnote);
        Assert.Contains("tutor-1", ctx.EmbedQueue.Enqueued);
    }

    [Fact]
    public async Task Reject_SetsRejectedStatusAndStoresReason()
    {
        var ctx = CreateService();
        ctx.Db.Users.Add(NewUser("tutor-2"));
        ctx.Db.Tutorprofiles.Add(NewProfile("tutor-2", TutorProfileStatus.Draft, rejectionNote: null));
        await ctx.Db.SaveChangesAsync();

        var result = await ctx.Service.ApproveTutorProfileAsync("tutor-2", new ApproveTutorRequest { IsApproved = false, Reason = "Thiếu chứng chỉ" }, AdminId);

        Assert.Equal("Rejected", result.IsApproved);
        var profile = await ctx.Db.Tutorprofiles.AsNoTracking().SingleAsync(p => p.Tutorid == "tutor-2");
        Assert.Equal(TutorProfileStatus.Rejected, profile.Profilestatus);
        Assert.False(profile.Ispublic);
        Assert.Equal("Thiếu chứng chỉ", profile.Rejectionnote);
    }

    [Fact]
    public async Task RejectWithoutReason_StillRejectsAndStoresNullNote()
    {
        var ctx = CreateService();
        ctx.Db.Users.Add(NewUser("tutor-3"));
        ctx.Db.Tutorprofiles.Add(NewProfile("tutor-3", TutorProfileStatus.Draft, rejectionNote: null));
        await ctx.Db.SaveChangesAsync();

        var result = await ctx.Service.ApproveTutorProfileAsync("tutor-3", new ApproveTutorRequest { IsApproved = false, Reason = null }, AdminId);

        Assert.Equal("Rejected", result.IsApproved);
        var profile = await ctx.Db.Tutorprofiles.AsNoTracking().SingleAsync(p => p.Tutorid == "tutor-3");
        Assert.Equal(TutorProfileStatus.Rejected, profile.Profilestatus);
        Assert.Null(profile.Rejectionnote);
    }

    [Fact]
    public async Task ProfileExistsButUserMissing_ThrowsUserNotFoundException()
    {
        var ctx = CreateService();
        // Profile row without its owning User row - the service looks each up separately.
        ctx.Db.Tutorprofiles.Add(NewProfile("tutor-4", TutorProfileStatus.Draft, rejectionNote: null));
        await ctx.Db.SaveChangesAsync();

        await Assert.ThrowsAsync<MV.DomainLayer.Exceptions.UserNotFoundException>(
            () => ctx.Service.ApproveTutorProfileAsync("tutor-4", new ApproveTutorRequest { IsApproved = true }, AdminId));
    }

    private static User NewUser(string id) => new()
    {
        Userid = id,
        Password = "hash",
        Fullname = "Gia sư",
        Primaryrole = UserRole.Tutor,
        Status = 1,
        Createdat = DateTime.UtcNow
    };

    private static Tutorprofile NewProfile(string tutorId, string status, string? rejectionNote) => new()
    {
        Tutorid = tutorId,
        Profilestatus = status,
        Ispublic = false,
        Rejectionnote = rejectionNote,
        Createdat = DateTime.UtcNow
    };

    private static ServiceContext CreateService()
    {
        var db = TestSupport.CreateInMemoryContext("approve-tutor-profile");
        var unitOfWork = new UnitOfWork(db, new PasswordRepository(), Microsoft.Extensions.Logging.Abstractions.NullLogger<UnitOfWork>.Instance);
        var embedQueue = new FakeTutorEmbedQueue();
        var service = new UserService(
            unitOfWork,
            new PasswordRepository(),
            null!,
            null!,
            new FakeNotificationService(),
            null!,
            db,
            embedQueue);
        return new ServiceContext(service, db, embedQueue);
    }

    private sealed record ServiceContext(UserService Service, AgoraDbContext Db, FakeTutorEmbedQueue EmbedQueue);
}
