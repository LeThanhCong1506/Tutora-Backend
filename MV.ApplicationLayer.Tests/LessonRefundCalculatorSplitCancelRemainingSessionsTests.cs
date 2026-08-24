using MV.ApplicationLayer.Helpers;
using Xunit;

namespace MV.ApplicationLayer.Tests;

/// <summary>
/// Regression coverage for two real money-conservation bugs found while testing booking #287
/// (dev DB) — both in the same "hủy khóa học & hoàn tiền" split for 1 delivered / 4 undelivered
/// sessions out of 5 (Totalamount 250.000đ, Tutorfee 237.500đ, Finalprice 262.500đ, chỉ đóng cọc
/// 52.500đ):
///
/// Bug 1 — double payment: the parent-refund pool wasn't reduced by the delivered session's share
/// at all, so the tutor got 47.500đ (correct) AND the parent got their full 52.500đ deposit back
/// (wrong) — 100.000đ paid out of a booking that only ever escrowed 47.500đ.
///
/// Bug 2 — after fixing Bug 1, the pool was reduced using the NO-FEE per-session price (50.000đ)
/// instead of the WITH-FEE price (52.500đ, what the parent actually paid for that session incl. the
/// 5% service fee). A delivered session's service fee is earned by the platform, not refundable —
/// so the pool must shrink by the full with-fee amount. The bug left a 2.500đ gap that got refunded
/// to the parent instead of being kept by the platform, making the split (47.500 tutor + 5.000
/// parent) instead of the correct (47.500 tutor + 0 parent, platform keeps 5.000).
/// </summary>
public class LessonRefundCalculatorSplitCancelRemainingSessionsTests
{
    [Fact]
    public void OneDeliveredSessionOutOfFive_TutorKeepsShare_ParentGetsNothingBack()
    {
        // Exact numbers from booking #287: 5 sessions, only deposit paid (remaining never paid),
        // 1 session already delivered before the dispute cancelled the remaining 4. The deposit
        // (52.500đ) exactly equals 1 session's full price including the 5% parent fee (52.500đ) —
        // nothing is left over once that session is accounted for.
        var (deliveredTutorTarget, parentRefund) = LessonRefundCalculator.SplitCancelRemainingSessions(
            totalPaidByParent: 52_500m,
            totalAlreadyRefunded: 0m,
            parentRefundPerSessionNoFee: 50_000m,   // Totalamount(250_000) / 5
            parentRefundPerSessionWithFee: 52_500m, // Finalprice(262_500) / 5
            tutorEscrowPerSession: 47_500m,         // Tutorfee(237_500) / 5
            remainingCount: 4,
            deliveredCount: 1);

        Assert.Equal(47_500m, deliveredTutorTarget);
        Assert.Equal(0m, parentRefund);
        // The invariant that broke twice in production: tutor share + parent refund must never
        // exceed what the parent actually paid (the 5.000đ gap is the platform's fee — it stays
        // with the platform, going to neither wallet).
        Assert.True(deliveredTutorTarget + parentRefund <= 52_500m);
    }

    [Fact]
    public void NoDeliveredSessions_RefundsFullRemainingAmountAsBefore()
    {
        var (deliveredTutorTarget, parentRefund) = LessonRefundCalculator.SplitCancelRemainingSessions(
            totalPaidByParent: 52_500m,
            totalAlreadyRefunded: 0m,
            parentRefundPerSessionNoFee: 50_000m,
            parentRefundPerSessionWithFee: 52_500m,
            tutorEscrowPerSession: 47_500m,
            remainingCount: 5,
            deliveredCount: 0);

        Assert.Equal(0m, deliveredTutorTarget);
        Assert.Equal(52_500m, parentRefund);
    }

    [Fact]
    public void AllSessionsDelivered_LeavesNothingToRefund()
    {
        var (deliveredTutorTarget, parentRefund) = LessonRefundCalculator.SplitCancelRemainingSessions(
            totalPaidByParent: 262_500m,
            totalAlreadyRefunded: 0m,
            parentRefundPerSessionNoFee: 50_000m,
            parentRefundPerSessionWithFee: 52_500m,
            tutorEscrowPerSession: 47_500m,
            remainingCount: 0,
            deliveredCount: 5);

        Assert.Equal(237_500m, deliveredTutorTarget);
        Assert.Equal(0m, parentRefund);
    }
}
