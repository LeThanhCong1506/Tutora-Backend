using Microsoft.EntityFrameworkCore;
using MV.ApplicationLayer.Interfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.Entities;

namespace MV.ApplicationLayer.Helpers;

/// <summary>
/// Ngưỡng % đã học tối thiểu để được phép kích hoạt buổi phụ khi buổi gốc (Iscontinuation=false)
/// bị ngắt giữa chừng vì sự cố đột xuất, và thời lượng buổi phụ tương ứng — tách riêng khỏi
/// ClassSessionService để test được mà không cần dựng toàn bộ dependency graph của service đó.
/// Ngưỡng là điều kiện TỐI THIỂU để chống lạm dụng (không thể nghỉ lúc mới học 10% rồi coi là
/// "đột xuất"), không phải % thực tế sẽ được dùng để tính thời lượng buổi phụ — thời lượng buổi
/// phụ luôn là phần bù lý thuyết (1 - ngưỡng) của tổng thời lượng buổi gốc, bất kể đã học đúng
/// bao nhiêu % thật khi ngắt.
/// </summary>
public static class ClassSessionInterruptionPolicy
{
    /// <summary>Ngưỡng cho buổi thường: phải học được ít nhất 50% mới được ngắt.</summary>
    public const double DefaultThreshold = 0.50;

    /// <summary>Ngưỡng hạ thấp cho buổi đầu tiên (chưa Ismakeup/Iscontinuation/Isdisputerelearn,
    /// Scheduledstart sớm nhất) của booking: chỉ cần 20%.</summary>
    public const double FirstSessionThreshold = 0.20;

    public static double ThresholdFor(bool isFirstSessionOfBooking)
        => isFirstSessionOfBooking ? FirstSessionThreshold : DefaultThreshold;

    /// <summary>
    /// overlapRatio lấy từ SessionLogResponse.Summary.OverlapRatio (thời gian gia sư và học viên
    /// CÙNG có mặt / tổng thời lượng dự kiến) — cố ý không dùng Realstart→now, vì con số đó chỉ
    /// phản ánh phòng có mở hay không, không phản ánh có đang học thật hay không.
    /// </summary>
    public static bool MeetsThreshold(bool isFirstSessionOfBooking, double overlapRatio)
        => overlapRatio >= ThresholdFor(isFirstSessionOfBooking);

    public static TimeSpan ComputeContinuationDuration(bool isFirstSessionOfBooking, DateTime scheduledStart, DateTime scheduledEnd)
    {
        var remainingFraction = 1.0 - ThresholdFor(isFirstSessionOfBooking);
        var totalTicks = (scheduledEnd - scheduledStart).Ticks;
        return TimeSpan.FromTicks((long)(totalTicks * remainingFraction));
    }

    /// <summary>
    /// Dựng row ClassSession mới cho buổi phụ (link 2) — thuần tính toán, không đụng DB. Giờ mặc
    /// định là 1 tiếng sau thời điểm ngắt, để hai bên có thể vào học ngay hôm đó nếu rảnh; nếu
    /// không hợp, dùng luồng "Đề xuất đổi lịch" hiện có để dời (đã tự tương thích vì Status=Scheduled).
    /// </summary>
    public static ClassSession BuildContinuationSession(ClassSession original, bool isFirstSessionOfBooking, DateTime now)
    {
        var duration = ComputeContinuationDuration(isFirstSessionOfBooking, original.Scheduledstart, original.Scheduledend);
        var defaultStart = now.AddHours(1);

        return new ClassSession
        {
            Bookingid = original.Bookingid,
            Tutorid = original.Tutorid,
            Studentid = original.Studentid,
            Iscontinuation = true,
            Originalsessionid = original.Classsessionid,
            Lessonprice = 0,
            Status = ClassSessionStatus.Scheduled,
            Scheduledstart = defaultStart,
            Scheduledend = defaultStart.Add(duration),
            Createdat = now
        };
    }

    /// <summary>
    /// "Buổi đầu tiên của booking" = chưa có row nào khác cùng Bookingid (không tính buổi bù/buổi
    /// phụ/buổi học lại) với Scheduledstart sớm hơn. Không có field thứ tự sẵn trên ClassSession
    /// nên phải truy vấn theo Scheduledstart.
    /// </summary>
    public static async Task<bool> IsFirstOriginalSessionAsync(
        IAppDbContext context, ClassSession session, CancellationToken cancellationToken = default)
    {
        var hasEarlierOriginalSession = await context.ClassSessions
            .AnyAsync(x =>
                x.Bookingid == session.Bookingid
                && x.Ismakeup != true
                && !x.Iscontinuation
                && !x.Isdisputerelearn
                && x.Scheduledstart < session.Scheduledstart, cancellationToken);

        return !hasEarlierOriginalSession;
    }
}
