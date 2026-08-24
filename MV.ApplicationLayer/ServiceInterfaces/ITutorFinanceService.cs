using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;

namespace MV.ApplicationLayer.ServiceInterfaces;

public interface ITutorFinanceService
{
    /// <summary>
    /// Aggregate finance summary for a tutor: total earned, pending, withdrawn.
    /// </summary>
    Task<FinanceSummaryResponse> GetSummaryAsync(string tutorId, CancellationToken ct = default);

    /// <summary>
    /// Earnings breakdown by period ("week" | "month" | "year") or custom date range.
    /// </summary>
    Task<EarningsResponse> GetEarningsAsync(string tutorId, string period, DateTime? from, DateTime? to, CancellationToken ct = default);

    /// <summary>
    /// Paged transaction history optionally filtered by type and date range.
    /// </summary>
    Task<TransactionHistoryPagedResponse> GetTransactionsAsync(string tutorId, int page, int pageSize, string? type, DateTime? from, DateTime? to, CancellationToken ct = default);

    /// <summary>
    /// Single transaction detail by id — ownership-checked against the tutor.
    /// </summary>
    Task<TransactionHistoryResponse> GetTransactionDetailAsync(string tutorId, int transactionId, CancellationToken ct = default);

    /// <summary>
    /// Net escrow currently held per booking (EscrowCredit - EscrowRelease - EscrowReversal, only
    /// bookings where the net is still positive) — for the "current escrow status" dashboard section.
    /// </summary>
    Task<EscrowStatusResponse> GetEscrowStatusAsync(string tutorId, CancellationToken ct = default);

    // Bank account CRUD moved to IBankAccountService (api/bank-account, shared with Parent/Student,
    // now OTP-gated).

    /// <summary>
    /// Submit a withdrawal request; runs fraud detection and trust scoring before queuing.
    /// </summary>
    Task<WithdrawalDetailResponse> CreateWithdrawalAsync(string tutorId, CreateWithdrawalRequest request, CancellationToken ct = default);

    /// <summary>
    /// Paged list of the tutor's past and pending withdrawal requests.
    /// </summary>
    Task<WithdrawalListResponse> GetWithdrawalsAsync(string tutorId, int page, int pageSize, CancellationToken ct = default);

    /// <summary>
    /// Single withdrawal detail by id — ownership-checked against the tutor.
    /// </summary>
    Task<WithdrawalDetailResponse> GetWithdrawalDetailAsync(string tutorId, int withdrawalId, CancellationToken ct = default);

    /// <summary>
    /// Cancel a pending withdrawal request (only possible before admin processes it).
    /// </summary>
    Task CancelWithdrawalAsync(string tutorId, int withdrawalId, CancellationToken ct = default);
}
