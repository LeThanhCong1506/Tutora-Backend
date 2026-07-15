namespace MV.DomainLayer.Constants;

/// <summary>
/// Escrow payment status constants for the Booking.Paymentstatus column.
/// Tracks whether funds are held in escrow and what phase.
/// </summary>
public static class PaymentStatus
{
    /// <summary>Initial payment status when booking is created, before any payment.</summary>
    public const string Pending = "Pending";

    /// <summary>First-session payment held in escrow.</summary>
    public const string DepositEscrowed = "DepositEscrowed";

    /// <summary>Full amount held in escrow (both deposit + remaining paid).</summary>
    public const string Escrowed = "Escrowed";

    /// <summary>Escrow is in holding state (intermediate wallet hold).</summary>
    public const string Holding = "holding";

    /// <summary>Payment completed and released (legacy / PayOS paid status).</summary>
    public const string Paid = "paid";
}
