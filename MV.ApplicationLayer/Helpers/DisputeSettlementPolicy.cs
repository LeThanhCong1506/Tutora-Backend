using MV.DomainLayer.Constants;
using static MV.DomainLayer.Constants.ClassSessionStatus;

namespace MV.ApplicationLayer.Helpers;

/// <summary>
/// Shared policy for opening a dispute without corrupting the booking settlement counter.
/// A settled class session has already reduced <c>Sessionsremaining</c>; reopening it must
/// restore exactly one unresolved session so the eventual admin resolution can settle it once.
/// </summary>
public static class DisputeSettlementPolicy
{
    public static bool IsTerminalBooking(string? bookingStatus)
        => bookingStatus is BookingStatus.Completed
            or BookingStatus.Cancelled
            or BookingStatus.CancelledNoshow;

    public static bool IsEligibleClassSession(string? classSessionStatus)
        => classSessionStatus is PendingConfirmation or Completed;

    public static int SessionsRemainingAfterOpeningDispute(int? currentSessionsRemaining, bool wasSettled)
        => wasSettled
            ? checked(Math.Max(0, currentSessionsRemaining ?? 0) + 1)
            : Math.Max(0, currentSessionsRemaining ?? 0);
}
