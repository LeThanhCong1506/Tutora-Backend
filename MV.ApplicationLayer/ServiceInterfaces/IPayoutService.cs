namespace MV.ApplicationLayer.ServiceInterfaces;

public interface IPayoutService
{
    /// <summary>
    /// Create a payout for a withdrawal request
    /// </summary>
    Task<MV.DomainLayer.DTO.Payout.PayoutResult> CreatePayoutAsync(int withdrawalId, CancellationToken ct = default);

    /// <summary>
    /// Get payout status from PayOS
    /// </summary>
    Task<MV.DomainLayer.DTO.Payout.PayoutStatusResult> GetPayoutStatusAsync(string payoutId, CancellationToken ct = default);

    /// <summary>
    /// Process all pending payouts (called by background job)
    /// </summary>
    Task ProcessPendingPayoutsAsync(CancellationToken ct = default);

    /// <summary>
    /// Get PayOS balance
    /// </summary>
    Task<MV.DomainLayer.DTO.ResponseModel.Admin.PayOSBalanceResponse?> GetPayOSBalanceAsync(CancellationToken ct = default);
}
