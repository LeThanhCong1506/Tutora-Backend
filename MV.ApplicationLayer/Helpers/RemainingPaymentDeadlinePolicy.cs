namespace MV.ApplicationLayer.Helpers;

/// <summary>
/// Hạn thanh toán phần còn lại (Booking.Paymentdueat, lúc Status chuyển sang
/// pending_remaining_payment) mặc định là 48h kể từ lúc kích hoạt, nhưng KHÔNG được vượt quá thời
/// điểm 2h trước giờ học của buổi <c>reserved</c> gần nhất của booking đó.
///
/// Lý do: sessions 2..N được tạo sẵn ở trạng thái reserved (không có Meetinglink, không join được
/// phòng — xem AgoraController) ngay từ lúc tạo booking, và chỉ được ActivateRemainingSessionsAsync
/// mở khoá khi thanh toán xong. Nếu phụ huynh đóng tiền sát hạn 48h, mà buổi kế tiếp đã lên lịch
/// sớm hơn nhiều so với 48h đó, buổi đó bị khoá suốt thời gian đợi rồi mở khoá vô nghĩa vì giờ học
/// đã trôi qua từ lâu — không ai từng vào được phòng trong đúng giờ đã đặt.
///
/// Dùng chung công thức này ở MỌI nơi tính/đọc Paymentdueat (2 nơi gán khi kích hoạt, 2 nơi đọc
/// fallback khi Paymentdueat null) để không lệch nhau.
/// </summary>
public static class RemainingPaymentDeadlinePolicy
{
    public const int DefaultHours = 48;

    /// <summary>Cùng độ đệm 2h trước giờ học đang dùng cho buffer đề xuất đổi lịch
    /// (ClassSessionRescheduleProposalService.MinHoursBeforeOriginalStart).</summary>
    public const int BufferHoursBeforeNextSession = 2;

    /// <param name="now">Thời điểm hiện tại (UTC).</param>
    /// <param name="earliestReservedSessionStart">
    /// Scheduledstart của buổi <c>reserved</c> sớm nhất của booking (null nếu không còn buổi
    /// reserved nào, ví dụ booking chỉ có 1 buổi).
    /// </param>
    public static DateTime ComputeDeadline(DateTime now, DateTime? earliestReservedSessionStart)
    {
        var deadline = now.AddHours(DefaultHours);

        if (earliestReservedSessionStart.HasValue)
        {
            var sessionCap = earliestReservedSessionStart.Value.AddHours(-BufferHoursBeforeNextSession);
            if (sessionCap < deadline)
                deadline = sessionCap;
        }

        // Không tạo ra hạn đã ở QUÁ KHỨ ngay từ lúc kích hoạt — nếu buổi kế tiếp quá gần (dưới 2h),
        // đây vốn là tình huống không thể cứu bằng cách chỉnh hạn (lưới an toàn thật sự là
        // ActivateRemainingSessionsAsync tự dời buổi bị lỡ giờ — xem PaymentService.Wallet.cs).
        return deadline < now ? now : deadline;
    }
}
