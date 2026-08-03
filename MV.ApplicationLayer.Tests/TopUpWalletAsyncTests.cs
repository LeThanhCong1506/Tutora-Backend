using Microsoft.Extensions.Logging.Abstractions;
using MV.ApplicationLayer.Services;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.Entities;
using MV.DomainLayer.Exceptions;
using MV.InfrastructureLayer.DBContext;
using Xunit;

namespace MV.ApplicationLayer.Tests;

// Maps to Excel sheet "TopUpWalletAsync" (Code_34, WalletService.ProcessTopupWebhookAsync).
// The payload/lookup/mismatch/idempotency checks below all run BEFORE the wallet row lock
// (FromSqlRaw(SqlQueries.LockWalletByUserId, ...)), so they're testable on EF InMemory. Only the
// actual balance-crediting success path needs a real Postgres connection and was verified separately.
public class TopUpWalletAsyncTests
{
    [Fact]
    public async Task NullData_ThrowsBookingException()
    {
        var service = CreateService(out _);
        var request = new PaymentWebhookRequest { Code = PayOSWebhookCode.SuccessCode, Success = true, Data = null! };

        var ex = await Assert.ThrowsAsync<BookingException>(() => service.ProcessTopupWebhookAsync(request, "{}"));
        Assert.Equal(WalletErrorCodes.InvalidAmount, ex.ErrorCode);
    }

    [Fact]
    public async Task NonSuccessCode_IsSilentNoOp()
    {
        var service = CreateService(out var db);
        var request = new PaymentWebhookRequest
        {
            Code = "01",
            Success = false,
            Data = new PayOSWebhookData { OrderCode = 555000222, Amount = 100000 }
        };

        await service.ProcessTopupWebhookAsync(request, "{}");

        Assert.Empty(db.Topuprequests);
        Assert.Empty(db.Wallettransactions);
    }

    [Fact]
    public async Task TopupNotFound_ThrowsBookingException()
    {
        var service = CreateService(out _);
        var request = new PaymentWebhookRequest
        {
            Code = PayOSWebhookCode.SuccessCode,
            Success = true,
            Data = new PayOSWebhookData { OrderCode = 555000333, Amount = 100000 }
        };

        var ex = await Assert.ThrowsAsync<BookingException>(() => service.ProcessTopupWebhookAsync(request, "{}"));
        Assert.Equal(WalletErrorCodes.TopupNotFound, ex.ErrorCode);
        Assert.Equal(404, ex.HttpStatus);
    }

    [Fact]
    public async Task AmountMismatch_ThrowsBookingException()
    {
        var service = CreateService(out var db);
        db.Topuprequests.Add(new Topuprequest
        {
            Bookingid = 1,
            Paymentphase = PaymentRequestPhase.Deposit,
            Ordercode = 555000444,
            Userid = "user-1",
            Amount = 200000,
            Status = TopupStatus.Pending,
            Createdat = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        var request = new PaymentWebhookRequest
        {
            Code = PayOSWebhookCode.SuccessCode,
            Success = true,
            Data = new PayOSWebhookData { OrderCode = 555000444, Amount = 100000 }
        };

        var ex = await Assert.ThrowsAsync<BookingException>(() => service.ProcessTopupWebhookAsync(request, "{}"));
        Assert.Equal(WalletErrorCodes.AmountMismatch, ex.ErrorCode);
        Assert.Equal(409, ex.HttpStatus);
    }

    [Fact]
    public async Task AlreadyCompletedWithLedgerAndAudit_IsIdempotentNoOp()
    {
        var service = CreateService(out var db);
        var wallet = new Wallet { Userid = "user-1", Balance = 500000, Frozenbalance = 0, Lastupdated = DateTime.UtcNow };
        db.Wallets.Add(wallet);
        var topup = new Topuprequest
        {
            Bookingid = 1,
            Paymentphase = PaymentRequestPhase.Deposit,
            Ordercode = 555000555,
            Userid = "user-1",
            Amount = 100000,
            Status = TopupStatus.Completed,
            Createdat = DateTime.UtcNow
        };
        db.Topuprequests.Add(topup);
        await db.SaveChangesAsync();

        db.Wallettransactions.Add(new Wallettransaction
        {
            Walletid = wallet.Walletid,
            Amount = 100000,
            Transactiontype = TransactionType.Deposit,
            Referencetable = ReferenceTable.Topup,
            Referenceid = topup.Topuprequestid,
            Ordercode = topup.Ordercode,
            Createdat = DateTime.UtcNow
        });
        db.PaymentTransactions.Add(new PaymentTransaction
        {
            Userid = "user-1",
            Paymentmethod = PaymentTransactionMethod.PayOS,
            Purpose = PaymentTransactionPurpose.WalletTopup,
            Direction = PaymentTransactionDirection.Inbound,
            Status = PaymentTransactionStatus.Succeeded,
            Amount = 100000,
            Currency = Currency.Vnd,
            Ordercode = topup.Ordercode,
            Capturesource = PaymentCaptureSource.Webhook,
            Reconciliationstatus = PaymentReconciliationStatus.Matched,
            Createdat = DateTime.UtcNow,
            Paidat = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var request = new PaymentWebhookRequest
        {
            Code = PayOSWebhookCode.SuccessCode,
            Success = true,
            Data = new PayOSWebhookData { OrderCode = topup.Ordercode, Amount = 100000 }
        };

        await service.ProcessTopupWebhookAsync(request, "{}");

        Assert.Single(db.Wallettransactions);
        Assert.Single(db.PaymentTransactions);
    }

    private static WalletService CreateService(out AgoraDbContext db)
    {
        db = TestSupport.CreateInMemoryContext("topup-webhook");
        return new WalletService(
            db, null!,
            new FakeNotificationService(),
            new FakeFileStorageService(),
            NullLogger<WalletService>.Instance);
    }
}
