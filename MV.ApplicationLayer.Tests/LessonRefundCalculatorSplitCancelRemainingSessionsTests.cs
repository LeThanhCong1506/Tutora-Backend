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

    // ── Bảng tick thủ công: Admin/Staff phân bổ từng buổi ────────────────────────
    //
    // Khoá mốc: 10 buổi, gốc 500.000đ (50.000đ/buổi), phí 5% mỗi bên.
    //     phụ huynh trả 525.000đ → 52.500đ/buổi;  gia sư nhận 475.000đ → 47.500đ/buổi
    //
    // Khi Admin/Staff tick tay, deliveredCount/remainingCount KHÔNG còn suy từ trạng thái buổi học
    // mà là số ô đã tick. Công thức chia tiền bên dưới không đổi — đó là chủ ý: mọi trần bảo vệ
    // tiền (đã thu bao nhiêu, đã hoàn bao nhiêu) vẫn áp nguyên, nên tick sai cũng không thể chi
    // vượt số tiền phụ huynh thực trả.

    [Fact]
    public void TickTay_DaTraDot2_HoanTheoGiaGocKhongPhiDichVu()
    {
        // Tick 3 buổi cho gia sư, 7 buổi cho phụ huynh. Đã trả đủ 525.000đ.
        var (tutorTarget, parentRefund) = LessonRefundCalculator.SplitCancelRemainingSessions(
            totalPaidByParent: 525_000m,
            totalAlreadyRefunded: 0m,
            parentRefundPerSessionNoFee: 50_000m,   // đã trả đợt 2 → KHÔNG hoàn phí dịch vụ
            parentRefundPerSessionWithFee: 52_500m,
            tutorEscrowPerSession: 47_500m,
            remainingCount: 7,
            deliveredCount: 3);

        Assert.Equal(142_500m, tutorTarget);   // 3 × 47.500
        Assert.Equal(350_000m, parentRefund);  // 7 × 50.000

        // Nền tảng giữ 32.500đ = 3 × 2.500 phí sàn + 7 × 2.500 phí dịch vụ + 3 × 2.500 phí dịch vụ
        // của buổi đã dạy. Kiểm tra bằng bất biến thay vì con số rời rạc.
        Assert.True(tutorTarget + parentRefund <= 525_000m);
    }

    [Fact]
    public void TickTay_ChuaTraDot2_HoanCaPhiDichVu()
    {
        // Mới đóng cọc 1 buổi (52.500đ), tick cả 10 buổi cho phụ huynh. Đơn giá hoàn là giá CÓ phí
        // dịch vụ, nhưng trần "chỉ thu được 52.500đ" vẫn chặn — hoàn đúng phần đã thu.
        var (tutorTarget, parentRefund) = LessonRefundCalculator.SplitCancelRemainingSessions(
            totalPaidByParent: 52_500m,
            totalAlreadyRefunded: 0m,
            parentRefundPerSessionNoFee: 52_500m,   // chưa trả đợt 2 → hoàn CẢ phí dịch vụ
            parentRefundPerSessionWithFee: 52_500m,
            tutorEscrowPerSession: 47_500m,
            remainingCount: 10,
            deliveredCount: 0);

        Assert.Equal(0m, tutorTarget);
        Assert.Equal(52_500m, parentRefund);
    }

    [Fact]
    public void TickTay_TickSaiVanKhongChiVuotSoDaThu()
    {
        // Ca hiểm nhất của bảng tick tay: phụ huynh mới đóng cọc 52.500đ nhưng Admin/Staff tick
        // 1 buổi cho gia sư VÀ 9 buổi cho phụ huynh — tổng "danh nghĩa" là 47.500 + 472.500đ.
        // Nếu trần bị bỏ qua, hệ thống chi ra gần nửa triệu từ một khoản thu 52.500đ.
        var (tutorTarget, parentRefund) = LessonRefundCalculator.SplitCancelRemainingSessions(
            totalPaidByParent: 52_500m,
            totalAlreadyRefunded: 0m,
            parentRefundPerSessionNoFee: 52_500m,
            parentRefundPerSessionWithFee: 52_500m,
            tutorEscrowPerSession: 47_500m,
            remainingCount: 9,
            deliveredCount: 1);

        Assert.Equal(47_500m, tutorTarget);
        Assert.Equal(0m, parentRefund);        // buổi đã dạy đã ngốn trọn phần thu được
        Assert.True(tutorTarget + parentRefund <= 52_500m);
    }

    [Fact]
    public void TickTay_TickHetChoGiaSu_PhuHuynhKhongDuocHoan()
    {
        var (tutorTarget, parentRefund) = LessonRefundCalculator.SplitCancelRemainingSessions(
            totalPaidByParent: 525_000m,
            totalAlreadyRefunded: 0m,
            parentRefundPerSessionNoFee: 50_000m,
            parentRefundPerSessionWithFee: 52_500m,
            tutorEscrowPerSession: 47_500m,
            remainingCount: 0,
            deliveredCount: 10);

        Assert.Equal(475_000m, tutorTarget);
        Assert.Equal(0m, parentRefund);
        Assert.True(tutorTarget + parentRefund <= 525_000m);
    }

    [Fact]
    public void TickTay_BuoiDaChotTruocDo_VanDuocTinhChoGiaSu()
    {
        // Buổi đã chốt (Issettled = true) không nằm trong bảng tick vì kết quả đã định đoạt. Nhưng
        // nó PHẢI được cộng vào deliveredCount, nếu không gia sư mất trắng tiền của một buổi đã dạy
        // xong và đã được phụ huynh xác nhận — chỉ vì Admin/Staff không có ô nào để tick cho nó.
        //
        // Ở đây: 1 buổi đã chốt từ trước + 2 buổi Admin/Staff tick cho gia sư = 3 buổi.
        var (tutorTarget, parentRefund) = LessonRefundCalculator.SplitCancelRemainingSessions(
            totalPaidByParent: 525_000m,
            totalAlreadyRefunded: 0m,
            parentRefundPerSessionNoFee: 50_000m,
            parentRefundPerSessionWithFee: 52_500m,
            tutorEscrowPerSession: 47_500m,
            remainingCount: 7,
            deliveredCount: 3);

        Assert.Equal(142_500m, tutorTarget);
        Assert.Equal(350_000m, parentRefund);
        Assert.True(tutorTarget + parentRefund <= 525_000m);
    }
}
