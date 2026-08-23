using MV.DomainLayer.DTO.ResponseModel;

namespace MV.ApplicationLayer.ServiceInterfaces;

/// <summary>
/// Service interface for settlement and escrow management
/// </summary>
public interface ISettlementService
{
    /// <summary>
    /// Process auto-confirm for classSessions past their deadline
    /// Called by background job every 5 minutes
    /// </summary>
    Task<int> ProcessAutoConfirmAsync(CancellationToken ct = default);

    /// <summary>
    /// Settle a specific classSession - move money from frozen to balance
    /// </summary>
    Task<SettlementResultResponse> SettleClassSessionAsync(int classSessionId, string? confirmedBy = null);

    /// <summary>
    /// Settle a classSession during admin dispute resolution (side-with-tutor / Release).
    /// Skips the PendingConfirmation/Completed status guard so a Disputed/NoShow classSession can be settled.
    /// </summary>
    Task<SettlementResultResponse> SettleDisputedClassSessionAsync(int classSessionId, string? confirmedBy = null);

    /// <summary>
    /// Process refund for a classSession (partial or full)
    /// </summary>
    Task<SettlementResultResponse> ProcessRefundAsync(int classSessionId, int refundPercentage, string processedBy);

    /// <summary>
    /// Dry-run of ProcessRefundAsync — same clamped math (funding-phase and frozen-balance aware),
    /// no side effects. Used to preview a custom refund percentage before committing to it.
    /// </summary>
    Task<RefundPreviewResponse> PreviewRefundAsync(int classSessionId, int refundPercentage);

    /// <summary>
    /// Get pending settlements (classSessions ready for auto-confirm)
    /// </summary>
    Task<List<PendingClassSessionResponse>> GetPendingSettlementsAsync();

    /// <summary>
    /// Finalize booking early (parent chose not to pay for remaining sessions).
    /// Releases escrow for completed classSessions, cancels remaining classSessions, marks booking Completed.
    /// </summary>
    Task FinalizeBookingEarlyAsync(int bookingId, CancellationToken ct = default);

    /// <summary>
    /// Parent/student explicitly ends a course after delivered sessions and before
    /// paying the remaining phase. Delivered-session payments are not refunded.
    /// </summary>
    Task<bool> FinalizeBookingEarlyByUserAsync(
        int bookingId,
        string userId,
        string? reason = null,
        CancellationToken ct = default);

    /// <summary>
    /// Admin/staff hủy booking sau khi xác minh ngoài hệ thống rằng phụ huynh đã "nghỉ ngang".
    /// Giải ngân toàn bộ escrow còn lại (kể cả các buổi chưa dạy) cho gia sư.
    /// </summary>
    Task<bool> CancelGhostBookingAsync(
        int bookingId,
        string adminId,
        string? reason = null,
        CancellationToken ct = default);

    /// <summary>
    /// Hủy toàn bộ các buổi CHƯA diễn ra (Scheduled/Reserved) còn lại của một booking và hoàn cho
    /// phụ huynh theo GIÁ GỐC mỗi buổi (không gồm 5% phí dịch vụ). Buổi đã hoàn thành giữ nguyên
    /// (gia sư không mất tiền buổi đã dạy). Dùng chung cho cả "Hủy khóa học & hoàn tiền" (resolve
    /// dispute) và staff hủy booking do phụ huynh "nghỉ ngang" — <paramref name="bookingStatus"/>
    /// quyết định trạng thái cuối của booking (<c>BookingStatus.CancelledByDispute</c> hoặc
    /// <c>BookingStatus.CancelledByStaff</c>). PHẢI được gọi bên trong một transaction đang mở,
    /// với booking đã được lock (FOR UPDATE) bởi caller. Trả về tổng tiền đã hoàn cho phụ huynh
    /// (0 nếu booking đã terminal hoặc có buổi khác đang mid-flight — no-op, không exception).
    /// </summary>
    Task<decimal> CancelRemainingSessionsAsync(
        int bookingId, string processedBy, string bookingStatus, string? reason, CancellationToken ct = default);

    /// <summary>
    /// Dry-run của <see cref="CancelRemainingSessionsAsync"/> — cùng công thức, không side effect.
    /// Dùng để admin xem trước số tiền/số buổi trước khi resolve dispute bằng "Hủy khóa học & hoàn tiền".
    /// </summary>
    Task<CourseCancelPreviewResponse> PreviewCancelRemainingSessionsAsync(
        int bookingId, int? disputedSessionId = null, CancellationToken ct = default);
}
