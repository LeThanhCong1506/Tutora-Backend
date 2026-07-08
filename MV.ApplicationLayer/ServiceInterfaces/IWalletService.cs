using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;

namespace MV.ApplicationLayer.ServiceInterfaces;

public interface IWalletService
{
    /// <summary>
    /// Initiate a wallet top-up via ZaloPay; returns payment URL / QR data.
    /// </summary>
    Task<TopupResponse> CreateTopupRequestAsync(string userId, TopupRequest request);

    /// <summary>
    /// Process an inbound ZaloPay webhook for a wallet top-up transaction.
    /// </summary>
    Task ProcessTopupWebhookAsync(PaymentWebhookRequest request, CancellationToken ct = default);

    /// <summary>
    /// Current wallet balance and currency for a user.
    /// </summary>
    Task<WalletBalanceResponse> GetWalletBalanceAsync(string userId);

    /// <summary>
    /// Paged transaction history (top-ups, deductions, refunds) for a user.
    /// </summary>
    Task<TransactionHistoryPagedResponse> GetTransactionHistoryAsync(string userId, int page = 1, int pageSize = 20);

    /// <summary>
    /// Verify the HMAC-SHA256 signature on a raw ZaloPay webhook payload.
    /// </summary>
    Task<bool> VerifyWebhookSignatureAsync(string payload, string signature);
}
