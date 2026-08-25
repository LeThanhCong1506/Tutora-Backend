using System.Text.Json.Serialization;
using MV.DomainLayer.DTO.RequestModel;

namespace MV.DomainLayer.DTO.ResponseModel;

/// <summary>
/// What a tutor's suspension/block did to the courses they were still teaching:
/// which future sessions were cancelled, how much went back to each payer, and how
/// much escrow was pulled back out of the tutor's frozen balance.
/// </summary>
public class SuspensionRefundImpactResponse
{
    /// <summary>Bookings that had at least one session cancelled by the cascade.</summary>
    public int BookingsAffected { get; set; }

    /// <summary>Bookings closed outright because nothing teachable was left.</summary>
    public int BookingsClosed { get; set; }

    /// <summary>Future sessions moved to <c>cancelled</c>.</summary>
    public int SessionsCancelled { get; set; }

    /// <summary>Total credited back to payer wallets.</summary>
    public decimal TotalRefunded { get; set; }

    /// <summary>Total pulled back out of the tutor's frozen balance (never earnings).</summary>
    public decimal TotalEscrowReversed { get; set; }

    /// <summary>Escrow released to the tutor for sessions they had already delivered on a
    /// booking the cascade closed — money they keep despite the suspension.</summary>
    public decimal TotalEscrowReleasedToTutor { get; set; }

    /// <summary>Bookings left untouched because no payer wallet could be resolved — the
    /// cascade never cancels sessions it cannot pay back, so these need a manual decision.</summary>
    public List<int> BookingsNeedingManualReview { get; set; } = new();

    /// <summary>Per-booking breakdown, for the CMS summary and the audit trail.</summary>
    public List<SuspensionRefundBookingImpact> Bookings { get; set; } = new();

    /// <summary>Set when the cascade ran nested inside another financial flow (no-show
    /// handling, dispute resolution) and therefore could not send its own notifications.</summary>
    public bool NotificationsDeferred { get; set; }

    /// <summary>
    /// Notifications the cascade wants sent, held back until the owning transaction commits.
    /// Creating a notification writes to the database *and* immediately pushes over SignalR/FCM —
    /// a push cannot be un-sent, so announcing a refund before its transaction commits risks
    /// telling a parent about money that then rolls away. The transaction owner drains this via
    /// <c>ISuspensionRefundService.NotifyImpactAsync</c> once the money is durable.
    /// Not part of the API contract.
    /// </summary>
    [JsonIgnore]
    public List<NotificationRequest> PendingNotifications { get; set; } = new();
}

public class SuspensionRefundBookingImpact
{
    public int BookingId { get; set; }
    public string? RefundRecipientId { get; set; }
    public int SessionsCancelled { get; set; }
    public decimal RefundAmount { get; set; }
    public decimal EscrowReversed { get; set; }

    /// <summary>Booking status after the cascade — unchanged when the course survives
    /// a temporary suspension, <c>completed</c>/<c>cancelled</c> when it was closed.</summary>
    public string? BookingStatus { get; set; }
    public bool Closed { get; set; }
}
