using Microsoft.Extensions.Logging.Abstractions;
using MV.ApplicationLayer.Services;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.Entities;
using MV.InfrastructureLayer.DBContext;
using Xunit;

namespace MV.ApplicationLayer.Tests;

// Maps to Excel sheet "ResolveDisputeAsync" (Code_30, DisputeService.ResolveDisputeAsync).
// Only the two request-shape validations run before the Serializable transaction that locks
// booking/dispute/class-session rows via FromSqlRaw(... FOR UPDATE) - Postgres-only syntax the
// InMemory provider cannot execute. The Release/Refund50/Refund100/Custom settlement paths and
// the "already resolved" guard all sit inside that lock and were verified against real Postgres.
public class ResolveDisputeAsyncTests
{
    [Fact]
    public async Task InvalidResolutionType_ThrowsArgumentException()
    {
        var db = TestSupport.CreateInMemoryContext("resolve-dispute");
        var service = CreateService(db);
        var request = new ResolveDisputeRequest { ResolutionType = "not_a_real_type" };

        await Assert.ThrowsAsync<ArgumentException>(() => service.ResolveDisputeAsync(1, "admin-1", request));
    }

    [Fact]
    public async Task CustomWithoutRefundPercentage_ThrowsArgumentException()
    {
        var db = TestSupport.CreateInMemoryContext("resolve-dispute");
        var service = CreateService(db);
        var request = new ResolveDisputeRequest { ResolutionType = ResolutionTypes.Custom, CustomRefundPercentage = null };

        await Assert.ThrowsAsync<ArgumentException>(() => service.ResolveDisputeAsync(1, "admin-1", request));
    }

    [Fact]
    public async Task DisputeNotFound_ThrowsArgumentException()
    {
        var db = TestSupport.CreateInMemoryContext("resolve-dispute");
        db.Disputes.Add(new Dispute
        {
            Disputeid = 1,
            Status = DisputeStatus.Pending,
            Disputetype = DisputeTypes.Quality,
            Reason = "test",
            Createdat = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        var service = CreateService(db);
        var request = new ResolveDisputeRequest { ResolutionType = ResolutionTypes.Release };

        await Assert.ThrowsAsync<ArgumentException>(() => service.ResolveDisputeAsync(999, "admin-1", request));
    }

    private static DisputeService CreateService(AgoraDbContext db) => new(
        null!, db, null!, null!, new FakeNotificationService(), new FakeFileStorageService(),
        null!, null!, null!, NullLogger<DisputeService>.Instance);
}
