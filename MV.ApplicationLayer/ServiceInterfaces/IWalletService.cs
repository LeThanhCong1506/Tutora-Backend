using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;

namespace MV.ApplicationLayer.ServiceInterfaces;

public interface IWalletService
{
    /// <summary>
    /// Initiate a wallet top-up; returns payment URL / QR data.
    /// </summary>
    Task<TopupResponse> CreateTopupRequestAsync(string userId, TopupRequest request);

    /// <summary>
    /// Process an inbound webhook for a wallet top-up transaction.
    /// </summary>
    Task ProcessTopupWebhookAsync(PaymentWebhookRequest request, CancellationToken ct = default);

    /// <summary>
    /// Trạng thái một lệnh nạp ví, có self-heal: hỏi PayOS trực tiếp và tự cộng ví nếu đã thanh toán
    /// nhưng webhook chưa về (hay gặp khi chạy localhost). FE poll endpoint này để biết khi nào cộng tiền xong.
    /// </summary>
    Task<TopupStatusResponse> GetTopupStatusAsync(long orderCode, string userId, CancellationToken ct = default);

    /// <summary>
    /// Current wallet balance and currency for a user.
    /// </summary>
    Task<WalletBalanceResponse> GetWalletBalanceAsync(string userId);

    /// <summary>
    /// Paged transaction history (top-ups, deductions, refunds) for a user.
    /// </summary>
    Task<TransactionHistoryPagedResponse> GetTransactionHistoryAsync(string userId, int page = 1, int pageSize = 20);

    /// <summary>
    /// Verify the HMAC-SHA256 signature on a raw webhook payload.
    /// </summary>
    Task<bool> VerifyWebhookSignatureAsync(string payload, string signature);
}

