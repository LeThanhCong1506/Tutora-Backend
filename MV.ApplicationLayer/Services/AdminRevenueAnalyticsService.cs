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

    /// <summary>Doanh thu ĐÃ GIAO trong kỳ: phí của các buổi đã settle.</summary>
    private static decimal RecognisedIn(
        IEnumerable<SessionFlat> sessions,
        Dictionary<int, BookingFlat> bookingById,
        DateTime fromUtc,
        DateTime toUtc) =>
        sessions
            .Where(s => s.Settled && s.When >= fromUtc && s.When < toUtc)
            .Sum(s => bookingById.TryGetValue(s.BookingId, out var b) ? FeePerSession(b) : 0);

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
