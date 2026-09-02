using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MV.ApplicationLayer.Services;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.Entities;
using MV.InfrastructureLayer.DBContext;
using MV.InfrastructureLayer.Repositories;
using Xunit;

namespace MV.ApplicationLayer.Tests;

/// <summary>
/// Mục D: khi buổi bị tranh chấp nằm trong 1 chuỗi buổi liên kết (buổi phụ/buổi bù/buổi học lại),
/// GetDisputeRecordingAsync phải trả về TOÀN BỘ chuỗi (không chỉ buổi gốc liền trước) để admin xem
/// trọn mọi phần buổi học liên quan.
/// </summary>
public class DisputeRecordingContinuationTests
{
    private const int DisputeId = 1;
    private const int OriginalSessionId = 10;
    private const int ContinuationSessionId = 11;
    private const string ActorId = "admin-1";

    [Fact]
    public async Task DisputedSessionIsContinuation_ChainIncludesOriginalSessionToo()
    {
        await using var db = CreateContext();
        SeedOriginalSession(db, hasRecording: true);
        SeedContinuationSession(db, hasRecording: false);
        SeedDispute(db, disputedClassSessionId: ContinuationSessionId);
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var response = await service.GetDisputeRecordingAsync(DisputeId, ActorId);

        Assert.Equal(2, response.Chain.Count);

        var original = Assert.Single(response.Chain, i => i.ClassSessionId == OriginalSessionId);
        Assert.Equal("available", original.Status);
        Assert.True(original.Available);
        Assert.False(original.IsCurrent);
        Assert.NotNull(original.StreamUrl);
        Assert.Contains($"/class-sessions/{OriginalSessionId}/recording/stream", original.StreamUrl);

        var continuation = Assert.Single(response.Chain, i => i.ClassSessionId == ContinuationSessionId);
        Assert.Equal("none", continuation.Status); // buổi phụ chưa có recording riêng
        Assert.False(continuation.Available);
        Assert.True(continuation.IsCurrent);
        Assert.Null(continuation.StreamUrl);
    }

    [Fact]
    public async Task DisputedSessionHasNoChain_ChainHasOnlyItself()
    {
        await using var db = CreateContext();
        SeedOriginalSession(db, hasRecording: true); // booking/tutor/student khác — không nằm cùng chuỗi
        SeedDispute(db, disputedClassSessionId: OriginalSessionId);
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var response = await service.GetDisputeRecordingAsync(DisputeId, ActorId);

        var item = Assert.Single(response.Chain);
        Assert.Equal(OriginalSessionId, item.ClassSessionId);
        Assert.Equal("available", item.Status);
        Assert.True(item.Available);
        Assert.True(item.IsCurrent);
    }

    private static void SeedOriginalSession(AgoraDbContext db, bool hasRecording)
    {
        db.ClassSessions.Add(new ClassSession
        {
            Classsessionid = OriginalSessionId,
            Status = ClassSessionStatus.Interrupted,
            Scheduledstart = DateTime.UtcNow.AddHours(-1),
            Scheduledend = DateTime.UtcNow.AddMinutes(-30),
            Checkouttime = hasRecording ? DateTime.UtcNow.AddMinutes(-30) : null,
            Recordingurl = hasRecording ? "https://example.test/recording-original.mp4" : null,
        });
    }

    private static void SeedContinuationSession(AgoraDbContext db, bool hasRecording)
    {
        db.ClassSessions.Add(new ClassSession
        {
            Classsessionid = ContinuationSessionId,
            Iscontinuation = true,
            Originalsessionid = OriginalSessionId,
            Status = ClassSessionStatus.Completed,
            Scheduledstart = DateTime.UtcNow.AddMinutes(-20),
            Scheduledend = DateTime.UtcNow,
            Checkouttime = hasRecording ? DateTime.UtcNow : null,
            Recordingurl = hasRecording ? "https://example.test/recording-continuation.mp4" : null,
        });
    }

    private static void SeedDispute(AgoraDbContext db, int disputedClassSessionId)
    {
        db.Disputes.Add(new Dispute
        {
            Disputeid = DisputeId,
            Classsessionid = disputedClassSessionId,
            Status = DisputeStatus.Pending,
            Createdby = "parent-1",
        });
    }

    private static DisputeService CreateService(AgoraDbContext db)
        => new(
            disputeRepo: new DisputeRepository(db),
            context: db,
            settlementService: null!,
            warningService: null!,
            notificationService: null!,
            storageService: null!,
            hubContext: null!,
            classificationService: null!,
            recordingAccessTokenService: new FakeRecordingAccessTokenService(),
            backgroundJobClient: null!,
            sessionLogService: null!,
            logger: NullLogger<DisputeService>.Instance);

    private static AgoraDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AgoraDbContext>()
            .UseInMemoryDatabase($"dispute-recording-continuation-{Guid.NewGuid()}")
            .Options;
        return new DisputeRecordingTestDbContext(options);
    }

    private sealed class DisputeRecordingTestDbContext(DbContextOptions<AgoraDbContext> options)
        : AgoraDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<QuestionBank>().Ignore(x => x.Embedding);
            modelBuilder.Entity<TutoraKbChunk>().Ignore(x => x.Embedding);
        }
    }

    private sealed class FakeRecordingAccessTokenService : IRecordingAccessTokenService
    {
        public string Issue(int classSessionId, string userId, TimeSpan lifetime) => $"token-{classSessionId}-{userId}";
        public bool TryValidate(string token, int classSessionId, out string? userId)
            => throw new NotImplementedException();
    }
}
