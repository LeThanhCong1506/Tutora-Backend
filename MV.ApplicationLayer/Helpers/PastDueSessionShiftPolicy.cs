namespace MV.ApplicationLayer.Helpers;

/// <summary>
/// Lưới an toàn cho ActivateRemainingSessionsAsync (PaymentService.Wallet.cs): nếu thanh toán phần
/// còn lại về trễ tới mức 1 buổi <c>reserved</c> đã trôi qua giờ học dự kiến, không được kích hoạt
/// (Scheduled) buổi đó với giờ cũ — không ai từng vào phòng đúng giờ đã đặt, coi như mất buổi vô ích.
/// Tách riêng khỏi PaymentService (là service rất nhiều dependency, chưa có test nào) để logic dời
/// giờ test được độc lập, không cần dựng cả PaymentService.
/// </summary>
public static class PastDueSessionShiftPolicy
{
    public const int ShiftDays = 7;

    /// <summary>
    /// Dời (scheduledStart, scheduledEnd) tới tương lai, mỗi vòng +<see cref="ShiftDays"/> ngày,
    /// giữ nguyên giờ trong ngày và thời lượng, cho tới khi Start ở tương lai so với now.
    /// </summary>
    public static (DateTime NewScheduledStart, DateTime NewScheduledEnd) ShiftIntoFuture(
        DateTime scheduledStart, DateTime scheduledEnd, DateTime now)
    {
        var duration = scheduledEnd - scheduledStart;
        var shiftedStart = scheduledStart;
        do
        {
            shiftedStart = shiftedStart.AddDays(ShiftDays);
        } while (shiftedStart <= now);

        return (shiftedStart, shiftedStart.Add(duration));
    }
}
