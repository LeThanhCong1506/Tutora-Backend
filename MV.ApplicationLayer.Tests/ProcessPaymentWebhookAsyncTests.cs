using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MV.ApplicationLayer.Services;
using MV.DomainLayer.Configuration;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.Exceptions;
using MV.InfrastructureLayer.DBContext;
using MV.InfrastructureLayer.Repositories;
using PayOS;
using Xunit;

namespace MV.ApplicationLayer.Tests;

// Maps to Excel sheet "ProcessPaymentWebhookAsync" (Code_36, PaymentService.ProcessWebhookAsync).
// PayOSClient's constructor never makes a network call, so a dummy instance is safe here.
// Only the branch that confirms a deposit/remaining payment against an EXISTING booking is
// blocked - that path locks the booking row via FromSqlRaw(SqlQueries.LockBookingById, ...).
// Everything before it (payload validation, non-success no-op, unrecognized order-code type,
// and recording an orphan transaction when the booking doesn't exist) is plain LINQ and is
// covered below.
public class ProcessPaymentWebhookAsyncTests
{
    [Fact]
    public async Task NullData_ThrowsInvalidWebhookPayload()
    {
        var ctx = CreateService();
        var request = new PaymentWebhookRequest { Code = "00", Success = true, Data = null! };

        var ex = await Assert.ThrowsAsync<BookingException>(() => ctx.Service.ProcessWebhookAsync(request, "{}"));
        Assert.Equal(BookingErrorCodes.InvalidWebhookPayload, ex.ErrorCode);
    }

    [Fact]
    public async Task NonSuccessCode_IsSilentNoOp()
    {
        var ctx = CreateService();
        var request = new PaymentWebhookRequest
        {
            Code = "01",
            Success = false,
            Data = new PayOSWebhookData { OrderCode = 100000420000, Amount = 100_000 }
        };

        await ctx.Service.ProcessWebhookAsync(request, "{}");

        Assert.Empty(ctx.Db.PaymentTransactions);
    }

    [Fact]
    public async Task UnrecognizedOrderCodeType_ThrowsInvalidInput()
    {
        var ctx = CreateService();
        var request = new PaymentWebhookRequest
        {
            Code = PayOSWebhookCode.SuccessCode,
            Success = true,
            Data = new PayOSWebhookData { OrderCode = 90000012345, Amount = 100_000, Reference = "FT1" }
        };

        var ex = await Assert.ThrowsAsync<BookingException>(() => ctx.Service.ProcessWebhookAsync(request, "{}"));
        Assert.Equal(BookingErrorCodes.InvalidInput, ex.ErrorCode);
    }

    [Fact]
    public async Task RecognizedOrderCodeButNoMatchingBooking_RecordsOrphanTransaction()
    {
        var ctx = CreateService();
        // Deposit order code (prefix 1) for booking 555, which is never seeded.
        var request = new PaymentWebhookRequest
        {
            Code = PayOSWebhookCode.SuccessCode,
            Success = true,
            Data = new PayOSWebhookData { OrderCode = 100005550000, Amount = 100_000, Reference = "FT2", PaymentLinkId = "link-1" }
        };

        await ctx.Service.ProcessWebhookAsync(request, "{}");

        var transaction = Assert.Single(ctx.Db.PaymentTransactions);
        Assert.Equal(PaymentReconciliationStatus.Orphan, transaction.Reconciliationstatus);
        Assert.Equal(100005550000, transaction.Ordercode);
    }

    private static ServiceContext CreateService()
    {
        var db = TestSupport.CreateInMemoryContext("process-payment-webhook");
        var payOSClient = new PayOSClient("test-client-id", "test-api-key", "test-checksum-key");
        var paymentSettings = Options.Create(new PaymentSettings { ReturnUrl = "https://test.local/return", CancelUrl = "https://test.local/cancel" });
        var service = new PaymentService(
            db,
            new BookingRepository(db),
            null!,
            paymentSettings,
            payOSClient,
            new FakeNotificationService(),
            null!,
            NullLogger<PaymentService>.Instance);
        return new ServiceContext(service, db);
    }

    private sealed record ServiceContext(PaymentService Service, AgoraDbContext Db);
}
