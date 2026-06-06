using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.ApplicationLayer.Interfaces;

namespace MV.ApplicationLayer.JobHandlers;

/// <summary>
/// Hangfire job handler for processing payout requests
/// </summary>
public class PayoutJobHandler
{
    private readonly IAppDbContext _context;
    private readonly IPayoutService _payoutService;
    private readonly ILogger<PayoutJobHandler> _logger;

    public PayoutJobHandler(
        IAppDbContext context,
        IPayoutService payoutService,
        ILogger<PayoutJobHandler> logger)
    {
        _context = context;
        _payoutService = payoutService;
        _logger = logger;
    }

    /// <summary>
    /// Process immediate payout (AUTO_APPROVE)
    /// Called by Hangfire immediately after withdrawal creation
    /// </summary>
    [AutomaticRetry(Attempts = 3, DelaysInSeconds = new[] { 5, 10, 20 })]
    public async Task ProcessImmediatePayoutAsync(int withdrawalId, CancellationToken ct = default)
    {
        var withdrawal = await _context.Withdrawalrequests
            .FirstOrDefaultAsync(w => w.Withdrawalid == withdrawalId, ct);

        if (withdrawal == null)
        {
            _logger.LogWarning("Withdrawal {WithdrawalId} not found", withdrawalId);
            return;
        }

        // Verify status still valid (accept both 'pending' from auto-approve and 'approved' from admin)
        if (withdrawal.Status != WithdrawalStatus.Pending && withdrawal.Status != WithdrawalStatus.Approved)
        {
            _logger.LogInformation("Withdrawal {WithdrawalId} status changed to {Status}, skipping",
                withdrawalId, withdrawal.Status);
            return;
        }

        _logger.LogInformation("Processing immediate payout for withdrawal {WithdrawalId}", withdrawalId);

        // Call payout service
        var result = await _payoutService.CreatePayoutAsync(withdrawal.Withdrawalid, ct);

        if (result.IsSuccess)
        {
                withdrawal.Processedby = SystemActors.SystemUpper;
            await _context.SaveChangesAsync(ct);

            _logger.LogInformation("Immediate payout completed for withdrawal {WithdrawalId}", withdrawalId);
        }
        else
        {
            _logger.LogError("Payout failed for withdrawal {WithdrawalId}: {Error}",
                withdrawalId, result.ErrorMessage);
            throw new Exception($"Payout failed: {result.ErrorMessage}");
        }
    }

    /// <summary>
    /// Process delayed payout (DELAYED)
    /// Called by Hangfire after 2 hours delay
    /// </summary>
    [AutomaticRetry(Attempts = 3, DelaysInSeconds = new[] { 5, 10, 20 })]
    public async Task ProcessDelayedPayoutAsync(int withdrawalId, CancellationToken ct = default)
    {
        var withdrawal = await _context.Withdrawalrequests
            .FirstOrDefaultAsync(w => w.Withdrawalid == withdrawalId, ct);

        if (withdrawal == null)
        {
            _logger.LogWarning("Withdrawal {WithdrawalId} not found", withdrawalId);
            return;
        }

        // Check admin intervention or user cancellation
        if (withdrawal.Status == WithdrawalStatus.Cancelled)
        {
            _logger.LogInformation("Withdrawal {WithdrawalId} was cancelled by user", withdrawalId);
            return;
        }

        if (withdrawal.Status == WithdrawalStatus.Approved)
        {
            _logger.LogInformation("Withdrawal {WithdrawalId} was already approved by admin", withdrawalId);
            return;
        }

        if (withdrawal.Status == WithdrawalStatus.Rejected)
        {
            _logger.LogInformation("Withdrawal {WithdrawalId} was rejected by admin", withdrawalId);
            return;
        }

        if (withdrawal.Status != WithdrawalStatus.Delayed)
        {
            _logger.LogInformation("Withdrawal {WithdrawalId} status changed to {Status}, skipping",
                withdrawalId, withdrawal.Status);
            return;
        }

        _logger.LogInformation("Processing delayed payout for withdrawal {WithdrawalId}", withdrawalId);

        // Change status to Pending for processing
        withdrawal.Status = WithdrawalStatus.Pending;
        await _context.SaveChangesAsync(ct);

        // Call payout service
        var result = await _payoutService.CreatePayoutAsync(withdrawal.Withdrawalid, ct);

        if (result.IsSuccess)
        {
                withdrawal.Processedby = SystemActors.SystemUpper;
            await _context.SaveChangesAsync(ct);

            _logger.LogInformation("Delayed payout completed for withdrawal {WithdrawalId}", withdrawalId);
        }
        else
        {
            _logger.LogError("Delayed payout failed for withdrawal {WithdrawalId}: {Error}",
                withdrawalId, result.ErrorMessage);
            throw new Exception($"Delayed payout failed: {result.ErrorMessage}");
        }
    }
}
