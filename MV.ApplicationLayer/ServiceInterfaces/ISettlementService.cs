using MV.DomainLayer.DTO.ResponseModel;

namespace MV.ApplicationLayer.ServiceInterfaces;

/// <summary>
/// Service interface for settlement and escrow management
/// </summary>
public interface ISettlementService
{
    /// <summary>
    /// Process auto-confirm for lessons past their deadline
    /// Called by background job every 5 minutes
    /// </summary>
    Task<int> ProcessAutoConfirmAsync(CancellationToken ct = default);

    /// <summary>
    /// Settle a specific lesson - move money from frozen to balance
    /// </summary>
    Task<SettlementResultResponse> SettleLessonAsync(int lessonId, string? confirmedBy = null);

    /// <summary>
    /// Process refund for a lesson (partial or full)
    /// </summary>
    Task<SettlementResultResponse> ProcessRefundAsync(int lessonId, int refundPercentage, string processedBy);

    /// <summary>
    /// Get pending settlements (lessons ready for auto-confirm)
    /// </summary>
    Task<List<PendingLessonResponse>> GetPendingSettlementsAsync();

    /// <summary>
    /// Finalize booking early (parent chose not to pay for remaining sessions).
    /// Releases escrow for completed lessons, cancels remaining lessons, marks booking Completed.
    /// </summary>
    Task FinalizeBookingEarlyAsync(int bookingId, CancellationToken ct = default);
}
