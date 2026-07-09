using MV.DomainLayer.Entities;

namespace MV.ApplicationLayer.Helpers;

public static class LessonRefundCalculator
{
    // Amount refunded to parent per session: Finalprice / sessions (what parent actually paid, incl. 5% fee).
    public static decimal ParentRefundPerSession(Booking b)
        => Math.Round((b.Finalprice ?? 0) / Math.Max(b.Totalsessions ?? 1, 1), 2);

    // Escrow released from tutor's FrozenBalance per session: Tutorfee / sessions (NET amount frozen).
    public static decimal TutorEscrowPerSession(Booking b)
        => Math.Round((b.Tutorfee ?? 0) / Math.Max(b.Totalsessions ?? 1, 1), 2);
}
