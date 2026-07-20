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
}
