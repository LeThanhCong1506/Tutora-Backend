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

    /// <summary>Ownership-checked wallet transaction detail: hoá đơn booking/dispute/withdrawal + chứng từ chi trả.</summary>
    Task<TransactionDetailResponse> GetTransactionDetailAsync(
        string userId,
        int transactionId,
        CancellationToken ct = default);

    /// <summary>
    /// Submit a withdrawal request for a Parent/Student wallet owner — the bank destination is
    /// the requester's saved BankAccount (see BankAccountController), snapshotted onto the
    /// withdrawal row. Throws BankInfoRequiredException if no bank account is saved yet.
    /// </summary>
    Task<WithdrawalDetailResponse> CreateWithdrawalAsync(string userId, CreateWithdrawalRequest request, CancellationToken ct = default);

    /// <summary>Paged list of the caller's own past and pending withdrawal requests.</summary>
    Task<WithdrawalListResponse> GetWithdrawalsAsync(string userId, int page, int pageSize, CancellationToken ct = default);

    /// <summary>Single withdrawal detail by id — ownership-checked, includes payout proof.</summary>
    Task<WithdrawalDetailResponse> GetWithdrawalDetailAsync(string userId, int withdrawalId, CancellationToken ct = default);
}

