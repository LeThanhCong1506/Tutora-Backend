using MV.DomainLayer.DTO.RequestModel.Admin;
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

    /// <summary>Exclusively claim a pending request before making the external bank transfer.</summary>
    Task<ApproveResult> ClaimRequestAsync(int withdrawalId, string actorUserId, CancellationToken ct = default);

    /// <summary>Release a request claimed by the current actor back to the review queue.</summary>
    Task<ApproveResult> ReleaseRequestAsync(int withdrawalId, string actorUserId, CancellationToken ct = default);

    /// <summary>
    /// Approve a withdrawal request. Decision stored in DB differs based on actorRole:
    /// Admin → ADMIN_APPROVED, Staff → STAFF_APPROVED.
    /// </summary>
    Task<ApproveResult> ApproveRequestAsync(
        int withdrawalId,
        string actorUserId,
        string actorRole,
        ApproveWithdrawalRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Reject a withdrawal request and refund the tutor's wallet.
    /// </summary>
    Task<RejectResult> RejectRequestAsync(int withdrawalId, string actorUserId, string reason, CancellationToken ct = default);

    /// <summary>
    /// Admin/staff chủ động cộng tiền vào ví một user (Tutor/Parent/Student), không gắn với
    /// booking hay yêu cầu rút tiền nào. Cộng thẳng vào số dư ngay, không qua bước duyệt thứ hai.
    /// </summary>
    Task<AdminWalletTransferResponse> TransferToUserAsync(
        string actorUserId,
        AdminWalletTransferRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Lịch sử các lần chuyển tiền chủ động, mới nhất trước.
    /// </summary>
    Task<AdminWalletTransferListResponse> GetTransferHistoryAsync(int page, int pageSize, CancellationToken ct = default);

    /// <summary>Số dư hiện tại của quỹ hệ thống.</summary>
    Task<SystemFundResponse> GetFundBalanceAsync(CancellationToken ct = default);

    /// <summary>
    /// Admin nạp tiền thật (kèm ảnh chứng minh) vào quỹ hệ thống — nguồn duy nhất mà
    /// <see cref="TransferToUserAsync"/> được phép trừ vào.
    /// </summary>
    Task<SystemFundTopupResponse> TopUpFundAsync(
        string actorUserId,
        SystemFundTopupRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Lịch sử các lần nạp quỹ, mới nhất trước.
    /// </summary>
    Task<SystemFundTopupListResponse> GetFundTopupHistoryAsync(int page, int pageSize, CancellationToken ct = default);
}
