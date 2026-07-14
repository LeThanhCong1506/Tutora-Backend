using MV.DomainLayer.Constants;
using MV.DomainLayer.Entities;

namespace MV.ApplicationLayer.Helpers;

public static class TutorResponseTimeoutPolicy
{
    public static bool CanProcess(Booking booking, DateTime now)
        => booking.Status == BookingStatus.PendingTutor
           && booking.Responsedeadline != null
           && booking.Responsedeadline <= now
           && booking.Refundstatus != RefundStatus.Refunded;

    public static decimal ParentRefundAmount(Booking booking)
        => booking.Paymentstatus == PaymentStatus.Escrowed || booking.Remainingpaidat != null
            ? booking.Finalprice ?? booking.Totalamount ?? 0
            : booking.Depositamount ?? 0;

    public static decimal TutorEscrowAmount(Booking booking)
        => booking.Paymentstatus == PaymentStatus.Escrowed || booking.Remainingpaidat != null
            ? booking.Tutorfee ?? 0
            : Math.Round((booking.Tutorfee ?? 0) / Math.Max(booking.Totalsessions ?? 1, 1), 2);
}
