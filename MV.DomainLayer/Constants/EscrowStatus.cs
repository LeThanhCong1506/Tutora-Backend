namespace MV.DomainLayer.Constants;

/// <summary>
/// Constants for the Booking.Escrowstatus column — booking-level escrow lifecycle.
/// Values must match the CMS admin display map (ESCROW_STATUS_MAP in bookingDisplay.ts).
/// </summary>
public static class EscrowStatus
{
    /// <summary>Funds are held in escrow (deposit or full amount).</summary>
    public const string Holding = "holding";

    /// <summary>Escrow fully paid out to the tutor (booking completed).</summary>
    public const string Released = "released";

    /// <summary>Escrow returned to the parent (decline / timeout / cancel / no-show).</summary>
    public const string Refunded = "refunded";
}
