using Microsoft.EntityFrameworkCore;
using MV.ApplicationLayer.Interfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.Entities;

namespace MV.ApplicationLayer.Helpers;

/// <summary>
/// Ngưỡng % đã học tối thiểu để được phép kích hoạt buổi phụ khi buổi gốc (Iscontinuation=false)
/// bị ngắt giữa chừng vì sự cố đột xuất — tách riêng khỏi ClassSessionService để test được mà
/// không cần dựng toàn bộ dependency graph của service đó. Ngưỡng CHỈ quyết định có được báo ngắt
/// hay không, KHÔNG ảnh hưởng tới thời lượng buổi phụ (xem ComputeContinuationDuration, tính theo
/// thời gian THẬT đã dạy + ngân sách gia hạn cố định).
///
/// Đã bỏ hẳn ngưỡng (còn 0% cho mọi buổi, kể cả không phải buổi đầu tiên): công thức
/// ComputeContinuationDuration giữ bất biến actualDelivered + continuationDuration =
/// totalScheduled + ContinuationExtensionMinutes bất kể ngắt sớm hay muộn, nên ngắt sớm KHÔNG cho
/// tổng thời lượng khả dụng nhiều hơn — không có rủi ro lạm dụng để cần chặn bằng ngưỡng % nữa
/// (quyết định sản phẩm, xác nhận lại sau khi rà công thức).
/// </summary>
public static class ClassSessionInterruptionPolicy
{
    /// <summary>Không còn ngưỡng tối thiểu — mọi buổi (kể cả không phải buổi đầu tiên của booking)
    /// đều báo ngắt được ngay từ 0% thời lượng.</summary>
    public const double DefaultThreshold = 0.0;

    /// <summary>Buổi đầu tiên (chưa Ismakeup/Iscontinuation/Isdisputerelearn, Scheduledstart sớm
    /// nhất) của booking: giữ nguyên 0% — vốn đã không có ngưỡng từ trước.</summary>
    public const double FirstSessionThreshold = 0.0;

    /// <summary>Ngân sách thời lượng THÊM cố định cho buổi phụ, không phụ thuộc buổi gốc dài bao
    /// lâu (buổi gốc 1, 2 hay 3 tiếng đều cộng thêm đúng 30 phút như nhau).</summary>
    public const int ContinuationExtensionMinutes = 30;

    public static double ThresholdFor(bool isFirstSessionOfBooking)
        => isFirstSessionOfBooking ? FirstSessionThreshold : DefaultThreshold;

    /// <summary>
    /// overlapRatio lấy từ SessionLogResponse.Summary.OverlapRatio (thời gian gia sư và học viên
    /// CÙNG có mặt / tổng thời lượng dự kiến) — cố ý không dùng Realstart→now, vì con số đó chỉ
    /// phản ánh phòng có mở hay không, không phản ánh có đang học thật hay không.
    /// </summary>
    public static bool MeetsThreshold(bool isFirstSessionOfBooking, double overlapRatio)
        => overlapRatio >= ThresholdFor(isFirstSessionOfBooking);

    /// <summary>
    /// Thời lượng buổi phụ = (thời lượng dự kiến buổi gốc - thời gian THẬT đã dạy trước khi ngắt)
    /// + ngân sách gia hạn cố định (<see cref="ContinuationExtensionMinutes"/>). VD buổi gốc 60
    /// phút, mới dạy thật 10 phút thì ngắt: buổi phụ = (60 - 10) + 30 = 80 phút.
    /// </summary>
    public static TimeSpan ComputeContinuationDuration(
        DateTime scheduledStart, DateTime scheduledEnd, DateTime checkInTime, DateTime interruptedAt)
    {
        var totalScheduled = scheduledEnd - scheduledStart;
        var actualDelivered = interruptedAt - checkInTime;
        if (actualDelivered < TimeSpan.Zero) actualDelivered = TimeSpan.Zero;

        var remaining = totalScheduled - actualDelivered;
        if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;

        return remaining + TimeSpan.FromMinutes(ContinuationExtensionMinutes);
    }

    /// <summary>
    /// Dựng row ClassSession mới cho buổi phụ (link 2) — thuần tính toán, không đụng DB. Giờ mặc
    /// định là 1 tiếng sau thời điểm ngắt, để hai bên có thể vào học ngay hôm đó nếu rảnh; nếu
    /// không hợp, dùng luồng "Đề xuất đổi lịch" hiện có để dời (đã tự tương thích vì Status=Scheduled).
    /// </summary>
    public static ClassSession BuildContinuationSession(ClassSession original, DateTime now)
    {
        var checkInTime = original.Checkintime ?? original.Scheduledstart;
        var duration = ComputeContinuationDuration(original.Scheduledstart, original.Scheduledend, checkInTime, now);
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
