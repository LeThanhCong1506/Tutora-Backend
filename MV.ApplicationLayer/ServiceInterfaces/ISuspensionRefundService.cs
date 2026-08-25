using MV.DomainLayer.DTO.ResponseModel;

namespace MV.ApplicationLayer.ServiceInterfaces;

/// <summary>
/// Settles the courses a tutor was still teaching when their account was taken offline
/// — by a warning-driven suspension, a manual suspension, or an admin block.
/// </summary>
public interface ISuspensionRefundService
{
    /// <summary>
    /// Cancels the sessions the tutor can no longer teach, refunds the payer, and pulls the
    /// matching escrow back out of the tutor's frozen balance.
    /// </summary>
    /// <param name="tutorId">The suspended/blocked tutor.</param>
    /// <param name="suspensionEndDate">
    /// When the tutor is expected back. Only sessions scheduled at or before this moment are
    /// cancelled, so a 7-day suspension does not wipe out a course that resumes afterwards.
    /// <c>null</c> means indefinite (permanent suspension or admin block) — every session the
    /// tutor has not delivered yet is cancelled.
    /// </param>
    /// <param name="reason">Shown to the payer in the refund notification and the ledger entry.</param>
    /// <remarks>
    /// Idempotent: it only ever acts on sessions still in <c>scheduled</c>/<c>reserved</c>, so
    /// running it twice (re-suspension, a retry, a block on an already-suspended tutor) is a no-op
    /// the second time. Joins an ambient transaction when the caller already opened one.
    /// </remarks>
    Task<SuspensionRefundImpactResponse> CascadeSuspensionAsync(
        string tutorId,
        DateTime? suspensionEndDate,
        string reason,
        CancellationToken ct = default);

    /// <summary>
    /// Sends the notifications a cascade held back, and clears them from the impact so a retrying
    /// caller cannot announce the same refund twice. Call it only after the transaction that owns
    /// the cascade has committed; safe (and a no-op) when there is nothing pending.
    /// </summary>
    Task NotifyImpactAsync(SuspensionRefundImpactResponse impact);

    /// <summary>
    /// Read-only projection of what <see cref="CascadeSuspensionAsync"/> would do, so an operator
    /// can see the financial impact before committing to a suspension. Moves no money.
    /// </summary>
    Task<SuspensionRefundImpactResponse> PreviewCascadeAsync(
        string tutorId,
        DateTime? suspensionEndDate,
        CancellationToken ct = default);
}
