using Microsoft.Extensions.Logging.Abstractions;
using MV.ApplicationLayer.Services;
using MV.DomainLayer.Constants;
using MV.DomainLayer.Entities;
using MV.DomainLayer.Exceptions;
using MV.InfrastructureLayer.DBContext;
using Xunit;

namespace MV.ApplicationLayer.Tests;

// Maps to Excel sheet "ConfirmClassSessionAsync" (Code_25, SettlementService.SettleClassSessionAsync).
// The actual settlement (SettleClassSessionInternalAsync) locks the tutor wallet via
// FromSqlRaw(SqlQueries.LockWalletByUserId, ...) - Postgres-only, needs a real DB. Covered here
// are the pre-checks that run before that call.
public class ConfirmClassSessionAsyncTests
{
    [Fact]
    public async Task UnknownSession_ThrowsClassSessionNotFound()
    {
        var db = TestSupport.CreateInMemoryContext("confirm-class-session");
        var service = new SettlementService(db, null!, null!, NullLogger<SettlementService>.Instance);

        await Assert.ThrowsAsync<ClassSessionException>(() => service.SettleClassSessionAsync(999));
    }

    [Fact]
    public async Task AlreadySettled_ThrowsClassSessionAlreadyConfirmed()
    {
        var db = TestSupport.CreateInMemoryContext("confirm-class-session");
        db.ClassSessions.Add(new ClassSession { Classsessionid = 1, Status = ClassSessionStatus.Completed, Issettled = true, Scheduledstart = DateTime.UtcNow.AddDays(-1), Scheduledend = DateTime.UtcNow.AddDays(-1).AddHours(1) });
        await db.SaveChangesAsync();
        var service = new SettlementService(db, null!, null!, NullLogger<SettlementService>.Instance);

        var ex = await Assert.ThrowsAsync<ClassSessionException>(() => service.SettleClassSessionAsync(1));
        Assert.Equal(ClassSessionErrorCodes.ClassSessionAlreadyConfirmed, ex.ErrorCode);
    }

    [Fact]
    public async Task WrongStatus_ThrowsInvalidClassSessionStatus()
    {
        var db = TestSupport.CreateInMemoryContext("confirm-class-session");
        db.ClassSessions.Add(new ClassSession { Classsessionid = 2, Status = ClassSessionStatus.Scheduled, Issettled = false, Scheduledstart = DateTime.UtcNow.AddDays(-1), Scheduledend = DateTime.UtcNow.AddDays(-1).AddHours(1) });
        await db.SaveChangesAsync();
        var service = new SettlementService(db, null!, null!, NullLogger<SettlementService>.Instance);

        var ex = await Assert.ThrowsAsync<ClassSessionException>(() => service.SettleClassSessionAsync(2));
        Assert.Equal(ClassSessionErrorCodes.InvalidClassSessionStatus, ex.ErrorCode);
    }
}
