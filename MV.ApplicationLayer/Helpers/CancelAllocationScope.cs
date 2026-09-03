using MV.DomainLayer.Entities;

namespace MV.ApplicationLayer.Helpers;

/// <summary>
/// Những buổi nào thực sự nằm trong phép chia tiền khi Admin/Staff hủy khóa học.
///
/// Khóa trả làm hai đợt: đợt 1 (cọc) chỉ mua một phần số buổi, đợt 2 mua phần còn lại. Chừng nào
/// đợt 2 chưa trả, các buổi ngoài phần cọc CHƯA HỀ ĐƯỢC THU TIỀN — không có gì để hoàn cho phụ
/// huynh và cũng không có escrow nào để giải ngân cho gia sư. Chúng chỉ đơn giản bị hủy.
///
/// Đưa chúng vào bảng tick sẽ khiến dòng tổng cộng dồn cả tiền chưa từng thu: booking #330 mới
/// đóng cọc 52.500đ (đúng 1 buổi) nhưng bảng hiện 10 dòng và tính ra "hoàn phụ huynh 525.000đ" —
/// con số không bao giờ chi được, và Admin/Staff lại đọc nó như một cam kết.
/// </summary>
public static class CancelAllocationScope
{
    /// <summary>
    /// Số tiền phụ huynh thực đã trả — cọc nếu chưa qua đợt 2, toàn bộ nếu đã qua.
    /// </summary>
    public static decimal TotalPaid(Booking booking)
        => booking.Remainingpaidat.HasValue
            ? (booking.Finalprice ?? 0)
            : (booking.Depositpaidat.HasValue ? (booking.Depositamount ?? 0) : 0m);

    /// <summary>
    /// Số buổi số tiền đó mua được, tính theo đơn giá CÓ phí dịch vụ (đúng số phụ huynh trả cho
    /// một buổi). Đã trả đợt 2 thì bằng toàn bộ số buổi của khóa.
    /// </summary>
    public static int PaidSessionCount(Booking booking)
    {
        var totalSessions = Math.Max(booking.Totalsessions ?? 1, 1);
        if (booking.Remainingpaidat.HasValue)
            return totalSessions;

        var perSessionWithFee = LessonRefundCalculator.ParentRefundPerSession(booking);
        if (perSessionWithFee <= 0)
            return 0;

        return Math.Clamp((int)Math.Floor(TotalPaid(booking) / perSessionWithFee), 0, totalSessions);
    }

    /// <summary>
    /// Buổi thứ <paramref name="sessionNumber"/> (1-based, sắp theo lịch) có nằm trong phần phải
    /// phân bổ không. Buổi ngoài phạm vi vẫn bị hủy cùng khóa, chỉ là không gắn với đồng nào.
    /// </summary>
    public static bool IsInScope(Booking booking, int sessionNumber)
        => sessionNumber <= PaidSessionCount(booking);
}
