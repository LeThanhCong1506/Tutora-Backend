using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;

namespace MV.ApplicationLayer.ServiceInterfaces;

public interface IWalletService
{
    /// <summary>
    /// Process an inbound webhook for a wallet top-up transaction.
    /// </summary>
    Task ProcessTopupWebhookAsync(PaymentWebhookRequest request, string rawPayload, CancellationToken ct = default);

    /// <summary>
    /// Trạng thái lệnh nạp phần thiếu gắn với một booking; có self-heal khi webhook PayOS chưa về.
    /// Không cung cấp API nạp ví độc lập.
    /// </summary>
    Task<TopupStatusResponse> GetBookingShortfallTopupStatusAsync(int bookingId, long orderCode, string userId, CancellationToken ct = default);

    /// <summary>
    /// Current wallet balance and currency for a user.
    /// </summary>
    Task<WalletBalanceResponse> GetWalletBalanceAsync(string userId);

    /// <summary>
    /// Paged transaction history (top-ups, deductions, refunds) for a user.
    /// </summary>
    Task<TransactionHistoryPagedResponse> GetTransactionHistoryAsync(string userId, int page = 1, int pageSize = 20);
}

