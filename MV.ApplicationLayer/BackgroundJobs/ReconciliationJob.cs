using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.Entities;
using MV.ApplicationLayer.Interfaces;

namespace MV.ApplicationLayer.BackgroundJobs;

/// <summary>
/// Reconciliation job to sync withdrawal status with PayOS
/// Runs every 30 minutes as safety net for edge cases
/// </summary>
public class ReconciliationJob : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ReconciliationJob> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromMinutes(30);

    public ReconciliationJob(
        IServiceProvider serviceProvider,
        ILogger<ReconciliationJob> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation("Công việc đối chiếu bắt đầu với khoảng thời gian: {Interval}", _interval);

        // Initial delay to let application start up
        await Task.Delay(TimeSpan.FromMinutes(5), ct);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ReconcilePayoutsAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi trong ReconciliationJob.");
            }

            await Task.Delay(_interval, ct);
        }

        _logger.LogInformation("ReconciliationJob dừng lại.");
    }

    private async Task ReconcilePayoutsAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var payoutService = scope.ServiceProvider.GetRequiredService<IPayoutService>();

        _logger.LogDebug("Starting reconciliation cycle");

        // Find stuck transactions: Pending with PayOS transaction ID but not updated in > 1 hour
        var stuckTransactions = await context.Withdrawalrequests
            .Where(w => w.Status == WithdrawalStatus.Pending
                && w.Payostransactionid != null
                && w.Lastretryat != null
                && w.Lastretryat < MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow.AddHours(-1))
            .ToListAsync(ct);

        if (!stuckTransactions.Any())
        {
            _logger.LogDebug("Không tìm thấy giao dịch bị kẹt nào.");
            return;
        }

        _logger.LogWarning("Thành lập {Count} giao dịch bị kẹt.", stuckTransactions.Count);

        foreach (var withdrawal in stuckTransactions)
        {
            try
            {
                await ReconcileSingleWithdrawalAsync(withdrawal, payoutService, context, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi đối chiếu khoản rút tiền {WithdrawalId}.", withdrawal.Withdrawalid);
            }
        }

        _logger.LogDebug("Hoàn tất chu kỳ đối chiếu.");
    }

    private async Task ReconcileSingleWithdrawalAsync(
        Withdrawalrequest withdrawal,
        IPayoutService payoutService,
        IAppDbContext context,
        CancellationToken ct)
    {
        _logger.LogInformation("Điều chỉnh việc rút tiền {WithdrawalId} với giao dịch PayOS {TransactionId}.",
            withdrawal.Withdrawalid, withdrawal.Payostransactionid);

        // Get status from PayOS
        var statusResult = await payoutService.GetPayoutStatusAsync(withdrawal.Payostransactionid!, ct);

        if (statusResult == null || string.IsNullOrEmpty(statusResult.Status))
        {
            _logger.LogWarning("Không thể lấy trạng thái PayOS cho giao dịch rút tiền {WithdrawalId}.",
                withdrawal.Withdrawalid);
            return;
        }

        _logger.LogInformation("Rút tiền đã được hòa giải {WithdrawalId}: Trạng thái PayOS = {Status}.",
            withdrawal.Withdrawalid, statusResult.Status);

        // Update local status based on PayOS status
        var statusChanged = false;

        if (statusResult.Status == PayoutConstants.PayOSStatus.Success && withdrawal.Status != WithdrawalStatus.Approved)
        {
            withdrawal.Status = WithdrawalStatus.Approved;
            withdrawal.Payosstatus = PayoutConstants.PayOSStatus.Success;
            withdrawal.Processedat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
            statusChanged = true;
        }
        else if (statusResult.Status == PayoutConstants.PayOSStatus.Failed && withdrawal.Status != WithdrawalStatus.Rejected)
        {
            withdrawal.Status = WithdrawalStatus.Rejected;
            withdrawal.Payosstatus = PayoutConstants.PayOSStatus.Failed;
            withdrawal.Payoserror = statusResult.ErrorMessage;
            statusChanged = true;
        }

        if (statusChanged)
        {
            await context.SaveChangesAsync(ct);
            _logger.LogInformation("Thông tin rút tiền được cập nhật {WithdrawalId} trạng thái thành {Status}.",
                withdrawal.Withdrawalid, withdrawal.Status);
        }

        // Check if stuck for > 4 hours, create system alert
        if (withdrawal.Lastretryat < MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow.AddHours(-4))
        {
            _logger.LogError("Rút tiền {WithdrawalId} bị kẹt > 4 giờ, tạo cảnh báo hệ thống.",
                withdrawal.Withdrawalid);

            var existingAlert = await context.Systemalerts
                .AnyAsync(a => a.Type == PayoutConstants.AlertTypes.StuckPayout
                    && a.Metadata!.Contains(withdrawal.Withdrawalid.ToString())
                    && !a.Resolved, ct);

            if (!existingAlert)
            {
                var alert = new Systemalert
                {
                    Type = PayoutConstants.AlertTypes.StuckPayout,
                    Severity = PayoutConstants.AlertSeverity.Critical,
                    Message = $"Rút tiền {withdrawal.Withdrawalid} bị kẹt > 4 giờ.",
                    Metadata = System.Text.Json.JsonSerializer.Serialize(new
                    {
                        withdrawalId = withdrawal.Withdrawalid,
                        payosTransactionId = withdrawal.Payostransactionid,
                        lastRetryAt = withdrawal.Lastretryat,
                        status = withdrawal.Status
                    }),
                    Resolved = false,
                    Createdat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow
                };

                context.Systemalerts.Add(alert);
                await context.SaveChangesAsync(ct);
            }
        }
    }
}
