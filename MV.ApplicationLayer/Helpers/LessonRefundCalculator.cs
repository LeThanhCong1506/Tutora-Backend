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

    // Amount refunded to parent per session WITHOUT the 5% service fee: Totalamount (giá gốc) /
    // sessions. Dùng khi hủy khóa học từ đợt thanh toán 2 trở đi (khác buổi học thử, ở đó hoàn cả
    // phí dịch vụ qua ParentRefundPerSession).
    public static decimal ParentRefundPerSessionNoFee(Booking b)
        => Math.Round((b.Totalamount ?? 0) / Math.Max(b.Totalsessions ?? 1, 1), 2);

    /// <summary>
    /// Chia tiền khi hủy các buổi CHƯA dạy của 1 booking (case 4 "Hủy khóa học & hoàn tiền" / case 6
    /// staff hủy do phụ huynh nghỉ ngang).
    ///
    /// Phần thuộc về (các) buổi ĐÃ dạy (<paramref name="deliveredCount"/>) PHẢI bị loại khỏi pool có
    /// thể hoàn cho phụ huynh trước khi tính hoàn — nếu không, tổng (giải ngân gia sư + hoàn phụ
    /// huynh) có thể vượt quá số tiền phụ huynh thực đã trả, tức hệ thống trả tiền từ hư không. Đây
    /// là bug thật đã xảy ra ở booking #287 (dev): escrow chỉ giữ 47.500đ (1 buổi) nhưng hệ thống
    /// trả ra 52.500đ hoàn cho phụ huynh + 47.500đ giải ngân cho gia sư = 100.000đ.
    ///
    /// QUAN TRỌNG: phần bị loại khỏi pool phải tính theo <paramref name="parentRefundPerSessionWithFee"/>
    /// (giá gốc CÓ gồm 5% phí dịch vụ — đúng bằng số tiền phụ huynh thực trả cho 1 buổi), KHÔNG phải
    /// giá gốc không phí. Buổi đã dạy thì phí dịch vụ của buổi đó hệ thống ăn trọn (không hoàn), chỉ
    /// buổi CHƯA dạy mới hoàn theo giá gốc không phí (<paramref name="parentRefundPerSessionNoFee"/>)
    /// — lần đầu code này viết sai dùng giá không-phí cho cả 2 vế, khiến phần 5% phí dịch vụ phụ
    /// huynh đã trả cho buổi đã dạy bị hoàn nhầm lại cho phụ huynh thay vì hệ thống giữ.
    /// </summary>
    public static (decimal DeliveredTutorTarget, decimal ParentRefund) SplitCancelRemainingSessions(
        decimal totalPaidByParent,
        decimal totalAlreadyRefunded,
        decimal parentRefundPerSessionNoFee,
        decimal parentRefundPerSessionWithFee,
        decimal tutorEscrowPerSession,
        int remainingCount,
        int deliveredCount)
    {
        var deliveredTutorTarget = Math.Round(tutorEscrowPerSession * deliveredCount, 2);
        var deliveredConsumedFromParent = Math.Round(parentRefundPerSessionWithFee * deliveredCount, 2);
        var maxParentRefund = Math.Max(0, totalPaidByParent - totalAlreadyRefunded - deliveredConsumedFromParent);
        var parentRefund = Math.Round(Math.Min(remainingCount * parentRefundPerSessionNoFee, maxParentRefund), 2);
        return (deliveredTutorTarget, parentRefund);
    }
}
