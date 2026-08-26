using MV.DomainLayer.DTO.ResponseModel;

namespace MV.ApplicationLayer.ServiceInterfaces;

/// <summary>
/// Settles the courses an account was still party to when it was taken offline — by a
/// warning-driven suspension, a manual suspension, or an admin block.
///
/// Applies to every role: a suspended tutor can no longer teach, and a suspended parent or
/// student can no longer attend. A suspended parent also takes their children's courses with
/// them, since the parent is the payer for those bookings.
/// </summary>
public interface ISuspensionRefundService
{
    /// <summary>
    /// Cancels the sessions that can no longer go ahead, refunds whoever paid for them, and pulls
    /// the matching escrow back out of the booking tutor's frozen balance.
    /// </summary>
    /// <param name="userId">The suspended/blocked account, in whichever role it holds.</param>
    /// <param name="suspensionEndDate">
    /// When the account is expected back. Only sessions scheduled at or before this moment are
    /// cancelled, so a 7-day suspension does not wipe out a course that resumes afterwards.
    /// <c>null</c> means indefinite (permanent suspension or admin block) — every session not
    /// delivered yet is cancelled.
    /// </param>
    /// <param name="reason">Shown to the payer in the refund notification and the ledger entry.</param>
    /// <remarks>
    /// Idempotent: it only ever acts on sessions still in <c>scheduled</c>/<c>reserved</c>, so
    /// running it twice (re-suspension, a retry, a block on an already-suspended account) is a no-op
    /// the second time. Joins an ambient transaction when the caller already opened one.
    /// </remarks>
    Task<SuspensionRefundImpactResponse> CascadeSuspensionAsync(
        string userId,
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
        string userId,
        DateTime? suspensionEndDate,
        CancellationToken ct = default);
}
