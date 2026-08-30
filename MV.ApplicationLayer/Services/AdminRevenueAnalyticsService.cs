using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.Interfaces;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.ResponseModel.Admin;
using MV.DomainLayer.Helpers;

namespace MV.ApplicationLayer.Services;

/// <summary>
/// Báo cáo doanh thu: hoa hồng booking + bán gói AI credit.
/// </summary>
public partial class AdminRevenueAnalyticsService(
    IAppDbContext context,
    ILogger<AdminRevenueAnalyticsService> logger) : IAdminRevenueAnalyticsService
{
    /// <summary>Booking đã phát sinh (hoặc đang phát sinh) doanh thu.</summary>
    private static readonly string[] RevenueBookingStatuses =
    [
        BookingStatus.Paid,
        BookingStatus.DepositPaid,
        BookingStatus.PendingRemainingPayment,
        BookingStatus.Ongoing,
        BookingStatus.Completed
    ];

    // Bản ghi trung gian
    /// <summary>Booking đã phẳng hoá, kèm số buổi đã giao — dùng lại cho mọi báo cáo.</summary>
    private sealed record BookingFlat(
        int BookingId,
        string? ParentId,
        string? StudentId,
        string? TutorId,
        int? SubjectId,
        int? GradeId,
        string? Status,
        decimal PlatformFee,
        decimal FinalPrice,
        decimal TutorFee,
        /// <summary>5% thu thêm của phụ huynh. FinalPrice trừ khoản này ra học phí gốc.</summary>
        decimal ParentFee,
        int TotalSessions,
        DateTime? CreatedAt,
        DateTime? PaymentDueAt,
        DateTime? DepositPaidAt,
        DateTime? RemainingPaidAt,
        DateTime? CancelledAt);

    private sealed record SessionFlat(
        int BookingId,
        string? TutorId,
        string? Status,
        bool Settled,
        DateTime When);

    private static string MonthKey(DateTime d) => $"{d.Month:00}/{d.Year}";

    /// <summary>Khách hàng: phụ huynh, hoặc học sinh nếu tự đặt lịch.</summary>
    private static string CustomerKey(BookingFlat b) => b.ParentId ?? b.StudentId!;

    /// <summary>Booking do học sinh tự đặt, không gắn phụ huynh.</summary>
    private static bool IsSelfBooking(BookingFlat b) => b.ParentId == null;

    /// <summary>Trả cọc rồi đứng yên quá số ngày này thì coi là đã chết.</summary>
    private const int StalledAfterDays = 14;

    /// <summary>Vượt được đợt 1. Xét status, không dùng DepositPaidAt vì mốc đó vẫn
    /// có ở booking hỏng.</summary>
    private static bool HasPassedDeposit(BookingFlat b) =>
        b.Status is BookingStatus.DepositPaid
            or BookingStatus.PendingRemainingPayment
            or BookingStatus.Paid
            or BookingStatus.Ongoing
            or BookingStatus.Completed;

    /// <summary>Trả cọc, học đúng 1 buổi rồi không trả tiếp. Xét class_sessions vì
    /// PaymentDueAt bị xoá khi trả cọc và booking dừng sớm bị đóng thành Completed.</summary>
    private static bool IsStalledAfterDeposit(BookingFlat b, DateTime asOf, int settledSessions)
    {
        // Đã trả đủ thì không còn là rủi ro đợt 2, kể cả sau đó có bỏ học.
        if (b.RemainingPaidAt != null) return false;

        // Đúng một buổi: học hết phần đợt 1 đã mua rồi dừng.
        if (settledSessions != 1) return false;

        // Booking một buổi thì đợt 1 đã là toàn bộ khoá — không tồn tại đợt 2 để dừng.
        if (b.TotalSessions <= 1) return false;

        if (b.DepositPaidAt == null) return false;

        // Chủ động dừng: huỷ, hoặc hệ thống đã đóng booking khi kết thúc sớm.
        if (b.CancelledAt != null && b.CancelledAt <= asOf) return true;
        if (b.Status is BookingStatus.Completed or BookingStatus.CancelledNoshow) return true;

        if (b.Status is not (BookingStatus.DepositPaid or BookingStatus.PendingRemainingPayment))
            return false;

        // Quá hạn trả đợt 2, hoặc để nằm im quá lâu.
        if (b.PaymentDueAt.HasValue && b.PaymentDueAt < asOf) return true;

        return b.DepositPaidAt.Value.AddDays(StalledAfterDays) < asOf;
    }

    /// <summary>Trả cọc nhưng chưa học buổi nào — dịch vụ chưa bắt đầu, khác
    /// <see cref="IsStalledAfterDeposit"/>.</summary>
    private static bool IsPaidButNeverStarted(BookingFlat b, DateTime asOf, int settledSessions)
    {
        if (settledSessions > 0) return false;
        if (b.RemainingPaidAt != null) return false;
        if (b.DepositPaidAt == null) return false;
        if (b.CancelledAt != null) return false;   // đã huỷ → thuộc thống kê hoàn tiền

        if (!HasPassedDeposit(b)) return false;

        return b.DepositPaidAt.Value.AddDays(StalledAfterDays) < asOf;
    }

    /// <summary>Mốc đầu tháng trong khoảng đang xem — biểu đồ bám range, không cứng 12 cột.</summary>
    private static List<DateTime> MonthBuckets(DateTime fromUtc, DateTime toUtc)
    {
        var cursor = new DateTime(fromUtc.Year, fromUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var last = new DateTime(toUtc.Year, toUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var months = new List<DateTime>();
        while (cursor <= last)
        {
            months.Add(cursor);
            cursor = cursor.AddMonths(1);
        }
        return months;
    }

    /// <summary>Độ mịn của trục thời gian trên biểu đồ.</summary>
    private enum BucketSize { Day, Week, Month }

    /// <summary>Một cột trên trục thời gian: khoảng [Start, End) và nhãn hiển thị.</summary>
    private sealed record TimeBucket(DateTime Start, DateTime End, string Label);

    /// <summary>
    /// Chia khoảng đang xem thành các mốc vẽ biểu đồ.
    ///
    /// Trước đây mọi biểu đồ đều chia theo tháng, nên khi người dùng chọn một tuần hoặc một
    /// khoảng vài ngày thì cả biểu đồ chỉ còn một cột — không đọc được gì.
    ///
    /// Ngưỡng lấy đúng theo dashboard (<c>AdminDashboardService</c>) để hai trang không cho ra
    /// hai cách chia khác nhau trên cùng một khoảng: ≤31 ngày xem theo ngày, ≤90 ngày theo
    /// tuần, dài hơn thì theo tháng.
    ///
    /// Nhãn của ba mức cố ý khác dạng nhau — "05/08", "05/08 – 11/08", "08/2026" — để người
    /// đọc biết ngay một cột đại diện cho bao lâu mà không cần chú thích riêng.
    /// </summary>
    private static List<TimeBucket> TimeBuckets(DateTime fromUtc, DateTime toUtc)
    {
        var totalDays = (toUtc.Date - fromUtc.Date).TotalDays + 1;
        var size = totalDays <= 31 ? BucketSize.Day
            : totalDays <= 90 ? BucketSize.Week
            : BucketSize.Month;

        var buckets = new List<TimeBucket>();

        if (size == BucketSize.Month)
        {
            foreach (var ms in MonthBuckets(fromUtc, toUtc))
            {
                buckets.Add(new TimeBucket(ms, ms.AddMonths(1), MonthKey(ms)));
            }
            return buckets;
        }

        if (size == BucketSize.Day)
        {
            for (var cursor = fromUtc.Date; cursor < toUtc; cursor = cursor.AddDays(1))
            {
                buckets.Add(new TimeBucket(cursor, cursor.AddDays(1), cursor.ToString("dd/MM")));
            }
            return buckets;
        }

        // Tuần bắt đầu từ thứ Hai, khớp cách chia tuần ISO của dashboard.
        var weekStart = fromUtc.Date;
        var dow = (int)weekStart.DayOfWeek;
        weekStart = weekStart.AddDays(-(dow == 0 ? 6 : dow - 1));
        while (weekStart < toUtc)
        {
            var end = weekStart.AddDays(7);
            buckets.Add(new TimeBucket(
                weekStart, end, $"{weekStart:dd/MM} – {end.AddDays(-1):dd/MM}"));
            weekStart = end;
        }
        return buckets;
    }

    /// <summary>Chuẩn hoá về UTC; mặc định 12 tháng gần nhất.</summary>
    private static (DateTime FromUtc, DateTime ToUtc) Normalise(DateTime? from, DateTime? to)
    {
        var toUtc = to.HasValue
            ? DateTime.SpecifyKind(to.Value, DateTimeKind.Utc)
            : TimeZoneHelper.UtcNow;
        var fromUtc = from.HasValue
            ? DateTime.SpecifyKind(from.Value, DateTimeKind.Utc)
            : toUtc.AddMonths(-12);
        return (fromUtc, toUtc);
    }

    /// <summary>Kỳ liền trước có cùng độ dài — dùng cho so sánh tăng trưởng.</summary>
    private static (DateTime FromUtc, DateTime ToUtc) PreviousPeriod(DateTime fromUtc, DateTime toUtc)
    {
        var span = toUtc - fromUtc;
        return (fromUtc - span, fromUtc);
    }

    private async Task<List<BookingFlat>> LoadBookingsAsync(CancellationToken ct) =>
        await context.Bookings
            .AsNoTracking()
            .Select(b => new BookingFlat(
                b.Bookingid,
                b.Parentid,
                b.Studentid,
                b.Tutorid,
                b.Tutorsubjectgradeprice != null ? b.Tutorsubjectgradeprice.Subjectid : (int?)null,
                b.Tutorsubjectgradeprice != null ? b.Tutorsubjectgradeprice.Gradelevelid : (int?)null,
                b.Status,
                b.Platformfee ?? 0,
                b.Finalprice ?? 0,
                b.Tutorfee ?? 0,
                b.Parentfee ?? 0,
                b.Totalsessions ?? 1,
                b.Createdat,
                b.Paymentdueat,
                b.Depositpaidat,
                b.Remainingpaidat,
                b.Cancelledat))
            .ToListAsync(ct);

    private async Task<List<SessionFlat>> LoadSessionsAsync(CancellationToken ct) =>
        await context.ClassSessions
            .AsNoTracking()
            .Where(l => l.Bookingid != null)
            // Buổi phụ và buổi học lại do khiếu nại nằm NGOÀI gói đã bán: chúng được xếp thêm
            // để bù cho một buổi hỏng, giá mỗi buổi bằng 0, người học không trả thêm đồng nào.
            // Đếm chúng vào đây thì số buổi đã settle vượt quá TotalSessions, và vì hoa hồng
            // mỗi buổi = PlatformFee / TotalSessions nên nền tảng ghi nhận doanh thu LỚN HƠN
            // khoản phí thực sự đã thu của chính booking đó (booking #285: thu 25.000, ghi
            // nhận 30.000).
            //
            // Buổi bù (Ismakeup) thì ngược lại: nó THAY THẾ một buổi đã bán bị dời, buổi gốc
            // không settle, nên vẫn phải tính — loại nó ra sẽ làm hụt doanh thu.
            //
            // Lọc ngay ở đây thay vì ở từng chỗ dùng: mọi phần của báo cáo doanh thu đều quy
            // buổi đã settle ra tiền hoặc ra tiến độ, không có chỗ nào cần đếm buổi ngoài gói.
            .Where(l => !l.Iscontinuation && !l.Isdisputerelearn)
            .Select(l => new SessionFlat(
                l.Bookingid!.Value,
                l.Tutorid,
                l.Status,
                l.Issettled == true,
                l.Realend ?? l.Scheduledstart))
            .ToListAsync(ct);

    /// <summary>Phí nền tảng của một buổi — chia đều như SettlementService chia escrow.</summary>
    private static decimal FeePerSession(BookingFlat b) =>
        b.TotalSessions <= 0 ? 0 : Math.Round(b.PlatformFee / b.TotalSessions, 2);

    /// <summary>
    /// Buổi này có được tính vào hoa hồng nền tảng không.
    ///
    /// Một chỗ duy nhất định nghĩa quy tắc, để các tab không tự đặt ra bộ lọc riêng rồi ra
    /// những con số lệch nhau như trước.
    /// </summary>
    private static bool IsRevenueSession(SessionFlat s, Dictionary<int, BookingFlat> bookingById) =>
        bookingById.TryGetValue(s.BookingId, out var b)
        && RevenueBookingStatuses.Contains(b.Status ?? "");

    /// <summary>
    /// Hoa hồng nền tảng của các buổi đã dạy xong và đã giải ngân trong kỳ.
    ///
    /// Chỉ tính buổi thuộc booking CÒN nằm trong nhóm phát sinh doanh thu. Trước đây hàm này
    /// tra toàn bộ booking nên gộp cả buổi thuộc lịch về sau bị hủy, trong khi tab Môn &amp; Lớp
    /// lại loại chúng ra — hai tab cùng đo một thứ mà lệch nhau đúng bằng khoản đó, không có
    /// chỗ nào giải thích.
    ///
    /// Khoản hoa hồng của lịch đã hủy không mất đi: nó được báo riêng qua
    /// <c>RevenueSummaryDto.CommissionFromCancelled</c> và hiện thành ghi chú trên giao diện,
    /// nên vẫn thấy được mà không làm lệch các con số cộng dồn.
    /// </summary>
    private static decimal RecognisedIn(
        IEnumerable<SessionFlat> sessions,
        Dictionary<int, BookingFlat> bookingById,
        DateTime fromUtc,
        DateTime toUtc) =>
        sessions
            .Where(s => s.Settled && s.When >= fromUtc && s.When < toUtc)
            .Sum(s => bookingById.TryGetValue(s.BookingId, out var b)
                      && RevenueBookingStatuses.Contains(b.Status ?? "")
                ? FeePerSession(b)
                : 0);

    /// <summary>Doanh thu ĐÃ KÝ trong kỳ: toàn bộ phí quy về ngày tạo booking.</summary>
    private static decimal ContractedIn(
        IEnumerable<BookingFlat> bookings,
        DateTime fromUtc,
        DateTime toUtc) =>
        bookings
            .Where(b => RevenueBookingStatuses.Contains(b.Status ?? "")
                        && b.CreatedAt >= fromUtc && b.CreatedAt < toUtc)
            .Sum(b => b.PlatformFee);

    private async Task<List<(decimal Amount, DateTime When)>> LoadAiPaymentsAsync(CancellationToken ct)
    {
        var rows = await context.PaymentTransactions
            .AsNoTracking()
            .Where(t => t.Purpose == PaymentTransactionPurpose.AiCreditPurchase
                        && t.Status == PaymentTransactionStatus.Succeeded)
            .Select(t => new { t.Amount, t.Paidat, t.Createdat })
            .ToListAsync(ct);

        return rows
            .Select(r => (r.Amount, When: r.Paidat ?? r.Createdat ?? TimeZoneHelper.UtcNow))
            .ToList();
    }
}
