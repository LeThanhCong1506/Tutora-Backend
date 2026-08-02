using Microsoft.Extensions.Logging.Abstractions;
using MV.ApplicationLayer.Services;
using MV.DomainLayer.Constants;
using MV.DomainLayer.Entities;
using MV.DomainLayer.Exceptions;
using Xunit;

namespace MV.ApplicationLayer.Tests;

// Maps to Excel sheet "SettleDisputedClassSessionAsync" (Code_43, SettlementService.SettleDisputedClassSessionAsync).
// Like ConfirmClassSessionAsync, the actual settlement locks the tutor wallet via
// FromSqlRaw(SqlQueries.LockWalletByUserId, ...) - needs a real DB. Only the pre-check below
// (already-settled) runs before that raw-SQL call.
public class SettleDisputedClassSessionAsyncTests
{
    [Fact]
    public async Task UnknownSession_ThrowsClassSessionNotFound()
    {
        var db = TestSupport.CreateInMemoryContext("settle-disputed-session");
        var service = new SettlementService(db, null!, null!, NullLogger<SettlementService>.Instance);

        await Assert.ThrowsAsync<ClassSessionException>(() => service.SettleDisputedClassSessionAsync(999));
    }

    [Fact]
    public async Task AlreadySettled_ThrowsClassSessionAlreadyConfirmed()
    {
        var db = TestSupport.CreateInMemoryContext("settle-disputed-session");
        db.ClassSessions.Add(new ClassSession { Classsessionid = 1, Status = ClassSessionStatus.Disputed, Issettled = true, Scheduledstart = DateTime.UtcNow.AddDays(-1), Scheduledend = DateTime.UtcNow.AddDays(-1).AddHours(1) });
        await db.SaveChangesAsync();
        var service = new SettlementService(db, null!, null!, NullLogger<SettlementService>.Instance);

        var ex = await Assert.ThrowsAsync<ClassSessionException>(() => service.SettleDisputedClassSessionAsync(1));
        Assert.Equal(ClassSessionErrorCodes.ClassSessionAlreadyConfirmed, ex.ErrorCode);
    }
}
