using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MV.ApplicationLayer.Services;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.Entities;
using MV.InfrastructureLayer;
using MV.InfrastructureLayer.DBContext;
using MV.InfrastructureLayer.Repositories;
using Xunit;

namespace MV.ApplicationLayer.Tests;

// Maps to Excel sheet "ReviewProfileUpdateRequestAsync" (Code_34, TutorService.ReviewProfileUpdateRequestAsync).
public class ReviewProfileUpdateRequestAsyncTests
{
    private const string TutorId = "tutor-1";

    [Fact]
    public async Task NoPendingRequest_ThrowsKeyNotFoundException()
    {
        var ctx = CreateService();
        await SeedTutorAsync(ctx.Db);

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => ctx.Service.ReviewProfileUpdateRequestAsync(TutorId, new AdminReviewProfileUpdateRequest { IsApproved = true }, "admin-1"));
    }

    [Fact]
    public async Task Approve_AppliesOnlyNonNullStagedFieldsAndClearsStaging()
    {
        var ctx = CreateService();
        await SeedTutorAsync(ctx.Db, bio: "Bio cũ", headline: "Headline cũ");
        ctx.Staging.Seed(TutorId, new PendingTutorProfileUpdate
        {
            TutorId = TutorId,
            Status = TutorProfileUpdateStatus.Pending,
            Headline = "Headline mới",
            Bio = null
        });

        var result = await ctx.Service.ReviewProfileUpdateRequestAsync(TutorId, new AdminReviewProfileUpdateRequest { IsApproved = true }, "admin-1");

        Assert.Equal(TutorProfileUpdateStatus.Approved, result.Status);
        Assert.False(result.HasNewerPendingChanges);
        var profile = await ctx.Db.Tutorprofiles.AsNoTracking().SingleAsync(p => p.Tutorid == TutorId);
        Assert.Equal("Headline mới", profile.Headline);
        Assert.Equal("Bio cũ", profile.Bio);
        Assert.Null(await ctx.Staging.GetPendingUpdateAsync(TutorId));
    }

    [Fact]
    public async Task Reject_DiscardsStagedEditsWithoutTouchingProfile()
    {
        var ctx = CreateService();
        await SeedTutorAsync(ctx.Db, bio: "Bio cũ", headline: "Headline cũ");
        ctx.Staging.Seed(TutorId, new PendingTutorProfileUpdate { TutorId = TutorId, Status = TutorProfileUpdateStatus.Pending, Headline = "Headline mới" });

        var result = await ctx.Service.ReviewProfileUpdateRequestAsync(TutorId, new AdminReviewProfileUpdateRequest { IsApproved = false, Note = "Chưa đạt" }, "admin-1");

        Assert.Equal(TutorProfileUpdateStatus.Rejected, result.Status);
        var profile = await ctx.Db.Tutorprofiles.AsNoTracking().SingleAsync(p => p.Tutorid == TutorId);
        Assert.Equal("Headline cũ", profile.Headline);
        Assert.Null(await ctx.Staging.GetPendingUpdateAsync(TutorId));
    }

    [Fact]
    public async Task StaleRawJson_StillApprovesButFlagsNewerPendingChanges()
    {
        var ctx = CreateService();
        await SeedTutorAsync(ctx.Db, headline: "Headline cũ");
        ctx.Staging.Seed(TutorId, new PendingTutorProfileUpdate { TutorId = TutorId, Status = TutorProfileUpdateStatus.Pending, Headline = "Headline mới" }, rawJson: "version-1");
        // Simulate tutor submitting a newer edit in the gap between the admin's read and the
        // later compare-and-delete call, so the compare-and-delete no longer matches version-1.
        ctx.Staging.MutateAfterNextReadTo = (new PendingTutorProfileUpdate { TutorId = TutorId, Status = TutorProfileUpdateStatus.Pending, Headline = "Headline mới hơn nữa" }, "version-2");

        var result = await ctx.Service.ReviewProfileUpdateRequestAsync(TutorId, new AdminReviewProfileUpdateRequest { IsApproved = true }, "admin-1");

        Assert.True(result.HasNewerPendingChanges);
        Assert.NotNull(await ctx.Staging.GetPendingUpdateAsync(TutorId));
    }

    private static async Task SeedTutorAsync(AgoraDbContext db, string? bio = null, string? headline = null)
    {
        db.Users.Add(new User { Userid = TutorId, Password = "hash", Fullname = "Gia sư", Primaryrole = UserRole.Tutor, Status = 1, Createdat = DateTime.UtcNow });
        db.Tutorprofiles.Add(new Tutorprofile { Tutorid = TutorId, Bio = bio, Headline = headline, Createdat = DateTime.UtcNow });
        await db.SaveChangesAsync();
    }

    private static ServiceContext CreateService()
    {
        var db = TestSupport.CreateInMemoryContext("review-profile-update");
        var unitOfWork = new UnitOfWork(db, new PasswordRepository(), NullLogger<UnitOfWork>.Instance);
        var staging = new FakeTutorProfileUpdateStagingService();
        var service = new TutorService(
            unitOfWork,
            new FakeFileStorageService(),
            null!,
            new FakeNotificationService(),
            NullLogger<TutorService>.Instance,
            null!,
            staging,
            db,
            null!,
            new FakeTutorEmbedQueue());
        return new ServiceContext(service, db, staging);
    }

    private sealed record ServiceContext(TutorService Service, AgoraDbContext Db, FakeTutorProfileUpdateStagingService Staging);
}
