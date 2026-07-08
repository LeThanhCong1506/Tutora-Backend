using MV.DomainLayer.DTO.ResponseModel.Admin;

namespace MV.ApplicationLayer.ServiceInterfaces;

public interface IAdminPayoutService
{
    /// <summary>
    /// Get overview dashboard metrics
    /// </summary>
    Task<PayoutOverviewResponse> GetOverviewAsync(CancellationToken ct = default);

    /// <summary>
    /// Get withdrawal requests that staff/admin can still manually approve or reject.
    /// </summary>
    Task<PendingReviewResponse> GetPendingReviewAsync(int page, int pageSize, CancellationToken ct = default);

    /// <summary>
    /// Get all withdrawal requests with filters
    /// </summary>
    Task<MV.DomainLayer.DTO.ResponseModel.WithdrawalListResponse> GetAllRequestsAsync(
        int page,
        int pageSize,
        string? status = null,
        string? search = null,
        DateTime? from = null,
        DateTime? to = null,
        CancellationToken ct = default);

    /// <summary>
    /// Get detailed withdrawal request info
    /// </summary>
    Task<AdminWithdrawalDetailResponse> GetRequestDetailAsync(int withdrawalId, CancellationToken ct = default);

    /// <summary>
    /// Approve a withdrawal request. Decision stored in DB differs based on actorRole:
    /// Admin → ADMIN_APPROVED, Staff → STAFF_APPROVED.
    /// </summary>
    Task<ApproveResult> ApproveRequestAsync(int withdrawalId, string actorUserId, string actorRole, string? note = null, CancellationToken ct = default);

    /// <summary>
    /// Reject a withdrawal request and refund the tutor's wallet.
    /// </summary>
    Task<RejectResult> RejectRequestAsync(int withdrawalId, string actorUserId, string reason, CancellationToken ct = default);
}
