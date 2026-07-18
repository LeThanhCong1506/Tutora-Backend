using MV.DomainLayer.DTO.ResponseModel.Admin;

namespace MV.ApplicationLayer.ServiceInterfaces;

public interface IAdminFinancialService
{
    /// <summary>
    /// Returns all financial KPI metrics for the admin dashboard.
    /// Supports optional date-range filtering and period grouping for trend data.
    /// </summary>
    /// <param name="from">Start of date range (UTC). Null = all-time for overview, last 12 months for trend.</param>
    /// <param name="to">End of date range (UTC). Null = now.</param>
    /// <param name="period">Trend grouping: "month" | "week" | "year". Default "month".</param>
    /// <param name="ct">Cancellation token.</param>
    Task<AdminFinancialMetricsResponse> GetMetricsAsync(
        DateTime? from,
        DateTime? to,
        string period,
        CancellationToken ct = default);

    /// <summary>
    /// Returns a paginated list of all wallet transactions across the platform.
    /// Supports filtering by transaction type, user id, date range, and keyword search.
    /// </summary>
    /// <param name="page">1-based page number.</param>
    /// <param name="pageSize">Number of items per page (max 100).</param>
    /// <param name="type">Filter by TransactionType constant. Null = all types.</param>
    /// <param name="userId">Filter by wallet owner user id. Null = all users.</param>
    /// <param name="from">Start of date range (UTC). Null = all-time.</param>
    /// <param name="to">End of date range (UTC). Null = now.</param>
    /// <param name="search">Keyword matched against Description (case-insensitive). Null = no filter.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<AdminTransactionListResponse> GetTransactionsAsync(
        int page,
        int pageSize,
        string? type,
        string? userId,
        DateTime? from,
        DateTime? to,
        string? search,
        CancellationToken ct = default);

    /// <summary>
    /// Returns actual bank/provider money movements. This is deliberately
    /// separate from the wallet ledger returned by GetTransactionsAsync.
    /// </summary>
    Task<AdminPaymentTransactionListResponse> GetPaymentTransactionsAsync(
        int page,
        int pageSize,
        string? paymentMethod,
        string? direction,
        string? purpose,
        string? status,
        string? reconciliationStatus,
        string? userId,
        int? bookingId,
        int? withdrawalId,
        int? paymentRequestId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        string? search,
        CancellationToken ct = default);

    /// <summary>Returns the complete audit record for one real-money transaction.</summary>
    Task<AdminPaymentTransactionDetailResponse> GetPaymentTransactionDetailAsync(
        int paymentTransactionId,
        CancellationToken ct = default);
}
