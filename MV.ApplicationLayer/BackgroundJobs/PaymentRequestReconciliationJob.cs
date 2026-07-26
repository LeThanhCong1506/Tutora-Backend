using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.Helpers;
using MV.ApplicationLayer.Interfaces;
using MV.ApplicationLayer.RepositoryInterfaces;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;

namespace MV.ApplicationLayer.BackgroundJobs;

/// <summary>
/// Reconciles legacy or ambiguous PayOS links after the expand migration.
/// A PostgreSQL advisory lock prevents multiple app instances from doing the
/// same provider calls concurrently.
/// </summary>
public sealed class PaymentRequestReconciliationJob(
    IServiceProvider serviceProvider,
    ILogger<PaymentRequestReconciliationJob> logger) : BackgroundService
{
    private const long AdvisoryLockId = 2026071601;
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ReconcileBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Payment request reconciliation batch failed.");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }

    private async Task ReconcileBatchAsync(CancellationToken ct)
    {
        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider
            .GetRequiredService<IAppDbContext>();
        var connection = context.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;

        if (openedHere)
            await connection.OpenAsync(ct);

        var acquired = false;
        try
        {
            await using (var lockCommand = connection.CreateCommand())
            {
                lockCommand.CommandText =
                    $"SELECT pg_try_advisory_lock({AdvisoryLockId})";
                acquired = await lockCommand.ExecuteScalarAsync(ct)
                    is true;
            }

            if (!acquired)
                return;

            var creationGraceCutoff =
                MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow.AddMinutes(-2);
            var requestIds = await context.PaymentRequests
                .AsNoTracking()
                // Đơn mua gói AI credit không thuộc booking — job đối soát này giả định
                // mọi đơn đều là booking (gọi FindBooking). Bỏ qua để tránh xử lý nhầm.
                .Where(r => r.Phase != PaymentRequestPhase.AiCredit)
                .Where(PaymentRequestReconciliationPolicy
                    .BuildCandidatePredicate(creationGraceCutoff))
                .OrderBy(r => r.Updatedat)
                .ThenBy(r => r.Createdat)
                .Take(50)
                .Select(r => r.Paymentrequestid)
                .ToListAsync(ct);

            foreach (var paymentRequestId in requestIds)
            {
                using var itemScope = serviceProvider.CreateScope();
                var paymentService = itemScope.ServiceProvider
                    .GetRequiredService<IPaymentService>();
                var itemContext = itemScope.ServiceProvider
                    .GetRequiredService<IAppDbContext>();
                var bookingRepository = itemScope.ServiceProvider
                    .GetRequiredService<IBookingRepository>();
                try
                {
                    await paymentService
                        .ReconcilePaymentRequestByIdAsync(
                            paymentRequestId,
                            ct);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(
                        ex,
                        "Could not reconcile payment request {PaymentRequestId}.",
                        paymentRequestId);
                    try
                    {
                        await using var tx = await itemContext.Database
                            .BeginTransactionAsync(
                                IsolationLevel.Serializable,
                                ct);
                        var failedRequest = await itemContext.PaymentRequests
                            .FirstOrDefaultAsync(r =>
                                r.Paymentrequestid == paymentRequestId,
                                ct);
                        if (failedRequest != null)
                        {
                            _ = await bookingRepository
                                .FindWithRelationsForUpdateAsync(
                                    failedRequest.Bookingid!.Value,
                                    ct);
                            await itemContext.PaymentRequests
                                .Entry(failedRequest)
                                .ReloadAsync(ct);
                            if (PaymentRequestStatus.IsActive(
                                failedRequest.Status))
                            {
                                failedRequest.Status =
                                    PaymentRequestStatus.RequiresReview;
                            }

                            // Rotate every failed candidate, including a
                            // SUPERSEDED request selected because it still has
                            // Partial/AmountMismatch captures. Otherwise its old
                            // timestamp can occupy the first batch forever and
                            // starve later requests.
                            failedRequest.Updatedat =
                                MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
                            await itemContext.SaveChangesAsync(ct);
                        }
                        await tx.CommitAsync(ct);
                    }
                    catch (Exception persistException)
                    {
                        logger.LogError(
                            persistException,
                            "Could not persist reconciliation failure for payment request {PaymentRequestId}.",
                            paymentRequestId);
                    }
                }
            }
        }
        finally
        {
            if (acquired)
            {
                await using var unlockCommand =
                    connection.CreateCommand();
                unlockCommand.CommandText =
                    $"SELECT pg_advisory_unlock({AdvisoryLockId})";
                await unlockCommand.ExecuteScalarAsync(
                    CancellationToken.None);
            }

            if (openedHere)
                await connection.CloseAsync();
        }
    }

}
