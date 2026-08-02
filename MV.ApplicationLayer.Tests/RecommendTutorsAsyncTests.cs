using Microsoft.Extensions.Logging.Abstractions;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.ApplicationLayer.Services;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.Entities;
using MV.InfrastructureLayer;
using MV.InfrastructureLayer.DBContext;
using MV.InfrastructureLayer.Repositories;
using Xunit;

namespace MV.ApplicationLayer.Tests;

// Maps to Excel sheet "RecommendTutorsAsync" (Code_35, TutorRecommendService.RecommendAsync).
public class RecommendTutorsAsyncTests
{
    [Fact]
    public async Task NoCandidates_ReturnsEmptyNotAiRanked()
    {
        var ctx = CreateService();

        var result = await ctx.Service.RecommendAsync(new TutorRecommendRequest { TopK = 10 });

        Assert.Empty(result.Tutors);
        Assert.Equal(0, result.Total);
        Assert.False(result.AiRanked);
    }

    [Fact]
    public async Task AiRankingSucceeds_ReturnsAiRankedTrueWithSimilarity()
    {
        var ctx = CreateService();
        SeedTutor(ctx.Db, "tutor-1", rating: 4.5);
        await ctx.Db.SaveChangesAsync();
        ctx.AiClient.RankResultToReturn = new List<AiRankedTutor> { new("tutor-1", 0.87f) };

        var result = await ctx.Service.RecommendAsync(new TutorRecommendRequest { TopK = 10, Query = "toán lớp 10" });

        Assert.True(result.AiRanked);
        var item = Assert.Single(result.Tutors);
        Assert.Equal("tutor-1", item.TutorId);
        Assert.Equal(0.87f, item.AiSimilarity);
    }

    [Fact]
    public async Task AiUnreachable_GracefullyDegradesToSqlOrder()
    {
        var ctx = CreateService();
        SeedTutor(ctx.Db, "tutor-2", rating: 4.0);
        await ctx.Db.SaveChangesAsync();
        ctx.AiClient.RankResultToReturn = null;

        var result = await ctx.Service.RecommendAsync(new TutorRecommendRequest { TopK = 10 });

        Assert.False(result.AiRanked);
        var item = Assert.Single(result.Tutors);
        Assert.Equal("tutor-2", item.TutorId);
        Assert.Null(item.AiSimilarity);
    }

    private static void SeedTutor(AgoraDbContext db, string tutorId, double rating)
    {
        db.Users.Add(new User { Userid = tutorId, Password = "hash", Fullname = "Gia sư " + tutorId, Primaryrole = UserRole.Tutor, Status = 1, Createdat = DateTime.UtcNow });
        db.Tutorprofiles.Add(new Tutorprofile
        {
            Tutorid = tutorId,
            Profilestatus = TutorProfileStatus.Active,
            Ispublic = true,
            Isacceptingbookings = true,
            Averagerating = rating,
            Createdat = DateTime.UtcNow
        });
    }

    private static ServiceContext CreateService()
    {
        var db = TestSupport.CreateInMemoryContext("recommend-tutors");
        var unitOfWork = new UnitOfWork(db, new PasswordRepository(), NullLogger<UnitOfWork>.Instance);
        var aiClient = new FakeTutorAiClient();
        var service = new TutorRecommendService(unitOfWork, db, aiClient, NullLogger<TutorRecommendService>.Instance);
        return new ServiceContext(service, db, aiClient);
    }

    private sealed record ServiceContext(TutorRecommendService Service, AgoraDbContext Db, FakeTutorAiClient AiClient);
}
