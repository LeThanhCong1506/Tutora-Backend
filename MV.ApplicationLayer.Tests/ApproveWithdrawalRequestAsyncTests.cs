using Microsoft.Extensions.Logging.Abstractions;
using MV.ApplicationLayer.Services;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel.Admin;
using MV.DomainLayer.Entities;
using MV.InfrastructureLayer.DBContext;
using Xunit;

namespace MV.ApplicationLayer.Tests;

// Maps to Excel sheet "ApproveWithdrawalRequestAsync" (Code_33, AdminPayoutService.ApproveRequestAsync).
// Every branch below is validated by a plain AsNoTracking() preview query executed BEFORE the
// transaction + FromSqlInterpolated(... FOR UPDATE) row lock, so all of them are testable on EF
// InMemory. Only the "claim/status changed under us since the preview read" race and the success
// path actually need the real row lock and were verified separately against Postgres.
public class ApproveWithdrawalRequestAsyncTests
{
    private const string ActorId = "staff-1";

    [Fact]
    public async Task MissingProofImage_ThrowsInvalidOperationException()
    {
        var service = CreateService(out _);
        var request = new ApproveWithdrawalRequest { PaidAt = DateTimeOffset.UtcNow, Note = "ok", ProofImage = null! };

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApproveRequestAsync(1, ActorId, "Staff", request));
    }

    [Fact]
    public async Task EmptyNote_ThrowsInvalidOperationException()
    {
        var service = CreateService(out _);
        var request = new ApproveWithdrawalRequest { PaidAt = DateTimeOffset.UtcNow, Note = "   ", ProofImage = TestSupport.FakeFormFile("proof.jpg") };

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApproveRequestAsync(1, ActorId, "Staff", request));
    }

    [Fact]
    public async Task WithdrawalNotFound_ThrowsKeyNotFoundException()
    {
        var service = CreateService(out _);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.ApproveRequestAsync(999, ActorId, "Staff", ValidRequest()));
    }

    [Fact]
    public async Task NotApprovedStatus_ThrowsInvalidOperationException()
    {
        var service = CreateService(out var db);
        db.Withdrawalrequests.Add(new Withdrawalrequest
        {
            Withdrawalid = 1,
            Userid = "tutor-1",
            Amount = 100000,
            Status = WithdrawalStatus.PendingReview,
            Claimedby = ActorId,
            Requestedat = DateTime.UtcNow.AddHours(-1)
        });
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApproveRequestAsync(1, ActorId, "Staff", ValidRequest()));
    }

    [Fact]
    public async Task ClaimedByOtherStaff_ThrowsInvalidOperationException()
    {
        var service = CreateService(out var db);
        db.Withdrawalrequests.Add(new Withdrawalrequest
        {
            Withdrawalid = 1,
            Userid = "tutor-1",
            Amount = 100000,
            Status = WithdrawalStatus.Approved,
            Claimedby = "other-staff",
            Requestedat = DateTime.UtcNow.AddHours(-1)
        });
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApproveRequestAsync(1, ActorId, "Staff", ValidRequest()));
    }

    [Fact]
    public async Task PaidAtInFuture_ThrowsInvalidOperationException()
    {
        var service = CreateService(out var db);
        db.Withdrawalrequests.Add(new Withdrawalrequest
        {
            Withdrawalid = 1,
            Userid = "tutor-1",
            Amount = 100000,
            Status = WithdrawalStatus.Approved,
            Claimedby = ActorId,
            Requestedat = DateTime.UtcNow.AddHours(-1)
        });
        await db.SaveChangesAsync();
        var request = ValidRequest();
        request.PaidAt = DateTimeOffset.UtcNow.AddHours(1);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApproveRequestAsync(1, ActorId, "Staff", request));
    }

    [Fact]
    public async Task PaidAtBeforeRequestedTime_ThrowsInvalidOperationException()
    {
        var service = CreateService(out var db);
        var requestedAt = DateTime.UtcNow.AddHours(-1);
        db.Withdrawalrequests.Add(new Withdrawalrequest
        {
            Withdrawalid = 1,
            Userid = "tutor-1",
            Amount = 100000,
            Status = WithdrawalStatus.Approved,
            Claimedby = ActorId,
            Requestedat = requestedAt
        });
        await db.SaveChangesAsync();
        var request = ValidRequest();
        request.PaidAt = new DateTimeOffset(requestedAt.AddHours(-1), TimeSpan.Zero);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApproveRequestAsync(1, ActorId, "Staff", request));
    }

    private static ApproveWithdrawalRequest ValidRequest() => new()
    {
        PaidAt = DateTimeOffset.UtcNow,
        Note = "Đã chuyển khoản",
        ProofImage = TestSupport.FakeFormFile("proof.jpg")
    };

    private static AdminPayoutService CreateService(out AgoraDbContext db)
    {
        db = TestSupport.CreateInMemoryContext("approve-withdrawal");
        return new AdminPayoutService(
            null!, null!, db,
            new FakeNotificationService(),
            new FakeFileStorageService(),
            NullLogger<AdminPayoutService>.Instance);
    }
}
