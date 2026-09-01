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
    /// <summary>
    /// Booking còn ĐANG chạy — hoa hồng của nó vẫn tính được bằng công thức hợp đồng.
    ///
    /// <c>pending_tutor</c> PHẢI có trong danh sách này. Đó là trạng thái ngay SAU khi phụ huynh
    /// trả cọc thành công (<c>PaymentService</c> đặt <c>Status = pending_tutor</c> cùng lúc với
    /// <c>Depositpaidat</c> và <c>Escrowstatus = holding</c>), tức khoá đang chờ gia sư bấm nhận
    /// chứ hoàn toàn chưa chốt sổ. Thiếu nó thì <see cref="IsBooksClosed"/> trả true cho MỌI
    /// booking vừa thanh toán, sổ ví bị đọc khi chưa có dòng hoàn/giải ngân nào, và
    /// <c>PlatformKept</c> đội lên tới trần <c>PlatformFee</c> — tức toàn bộ phí sàn của cả khoá
    /// được báo là đã thu, trong khi phụ huynh mới trả đúng một buổi và chưa buổi nào được dạy.
    ///
    /// Booking mới tạo mang <c>pending_payment</c> (<c>BookingService</c>) và nằm ngoài danh sách
    /// này — chưa ai trả đồng nào thì chưa có gì để ghi nhận.
    /// </summary>
    private static readonly string[] RevenueBookingStatuses =
    [
        BookingStatus.PendingTutor,
        BookingStatus.Paid,
        BookingStatus.DepositPaid,
        BookingStatus.PendingRemainingPayment,
        BookingStatus.Ongoing,
        BookingStatus.Completed
    ];

    private static bool IsLive(BookingFlat b) => RevenueBookingStatuses.Contains(b.Status ?? "");

    /// <summary>
    /// Booking đã CHỐT SỔ: sẽ không còn đồng nào chảy vào hay ra khỏi nó nữa.
    ///
    /// Hai dấu hiệu, và phải xét cả hai:
    ///   • status nằm ngoài <see cref="RevenueBookingStatuses"/> — mọi kiểu huỷ;
    ///   • escrow đã giải ngân hoặc hoàn hết — bắt được nhóm mà status vẫn là
    ///     <c>completed</c> nhưng thật ra đã dừng giữa chừng: gia sư bị đình chỉ
    ///     (<c>SuspensionRefundService</c>) hay khách bỏ dở sau đợt 1
    ///     (<c>SettlementService.FinalizeBookingEarlyByUserAsync</c>). Nếu chỉ xét status thì cả
    ///     nhóm này vẫn bị tính bằng công thức hợp đồng và HỤT đúng phần phí dịch vụ không hoàn.
    ///
    /// Escrow chỉ mở khi cả khoá kết thúc, nên booking đang chạy không bao giờ lọt vào đây kể cả
    /// khi đã có buổi settle — đó là điều kiện để tin được số liệu ví.
    /// </summary>
    private static bool IsBooksClosed(BookingFlat b) =>
        !IsLive(b) || b.EscrowStatus is EscrowStatus.Released or EscrowStatus.Refunded;

    /// <summary>Booking đang chạy và escrow chưa chốt — nhóm duy nhất còn sinh nợ dịch vụ.</summary>
    private static bool IsOpen(BookingFlat b) => !IsBooksClosed(b);

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
        /// <summary>Tiền đợt 1. Cần để biết booking đóng sổ giữa chừng đã thu được bao nhiêu.</summary>
        decimal DepositAmount,
        /// <summary>holding / released / refunded — mốc nhận biết escrow đã chốt xong.</summary>
        string? EscrowStatus,
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

    /// <summary>
    /// Một booking đã CHỐT SỔ (<see cref="IsBooksClosed"/>), kèm số tiền lấy thẳng từ SỔ VÍ —
    /// nguồn sự thật duy nhất dùng chung với <c>SettlementService</c>.
    ///
    ///     PlatformKept = clamp(CashIn − Refunded − TutorPaid, 0, PlatformFee)
    ///
    /// Ví dụ đã kiểm chứng (khoá 100.000đ/10 buổi, phí 5%+5%, dạy 1 buổi rồi admin huỷ):
    /// phụ huynh trả 105.000 → gia sư nhận 9.500 (1 buổi đã dạy) → hoàn phụ huynh 90.000
    /// (9 buổi chưa học, giá gốc không phí) → Tutora giữ 5.500 = hoa hồng 1.000 của buổi đã dạy
    /// + 4.500 phí dịch vụ không hoàn của 9 buổi bị huỷ. Công thức "đơn giá hoa hồng × số buổi
    /// đã dạy" chỉ nhìn thấy 1.000 — đó là lý do phải đọc ví.
    ///
    /// ─── Vì sao có <see cref="Adjustment"/> thay vì ghi thẳng PlatformKept ───────────
    ///
    /// Doanh thu phải ở lại ĐÚNG THÁNG dịch vụ được giao. Nếu quy cả PlatformKept về một mốc
    /// duy nhất thì một khoá mở tháng 1, dạy suốt tháng 2, đóng tháng 3 sẽ dồn toàn bộ doanh thu
    /// vào tháng 3 — biểu đồ xu hướng thành vô nghĩa và tháng 2 trống trơn.
    ///
    /// Nên chia làm ba mốc:
    ///   • phí phụ huynh ghi tại ngày MUỘN HƠN giữa (ngày trả đợt đó, ngày buổi ĐẦU dạy xong);
    ///   • phí gia sư của mỗi buổi ghi tại NGÀY DẠY, cho mọi booking, không phân biệt trạng thái;
    ///   • riêng phần chênh <c>Adjustment = PlatformKept − <see cref="EarnedSoFar"/></c> ghi tại
    ///     NGÀY CHỐT SỔ.
    ///
    /// Cộng lại vẫn đúng bằng PlatformKept. Với khoá hoàn tất bình thường thì Adjustment = 0,
    /// tức thay đổi này KHÔNG đụng gì tới nhóm chiếm đa số dữ liệu. Adjustment ÂM là hợp lệ:
    /// ca hoàn tiền một phần theo khiếu nại, nơi Tutora giữ ít hơn công thức.
    ///
    /// Khoá bị huỷ TRƯỚC buổi đầu (hoàn 100% kể cả phí) thì cả hai vế đều bằng 0 sẵn, nên
    /// Adjustment cũng là 0 — không có gì được ghi nhận rồi phải đảo ngược.
    /// </summary>
    private sealed record ClosedBooking(
        int BookingId,
        string? TutorId,
        int? SubjectId,
        int? GradeId,
        string? CustomerKey,
        /// <summary>Mốc ghi <see cref="Adjustment"/>: ngày huỷ, hoặc ngày buổi cuối được settle.</summary>
        DateTime When,
        /// <summary>Đóng sổ bằng một lệnh huỷ, khác với hoàn tất bình thường.</summary>
        bool Cancelled,
        decimal CashIn,
        decimal Refunded,
        decimal TutorPaid,
        decimal PlatformKept,
        /// <summary>Phần doanh thu chưa được các buổi đã dạy phản ánh. Có thể âm.</summary>
        decimal Adjustment,
        /// <summary>
        /// Phần <see cref="PlatformKept"/> thuộc về PHÍ PHỤ HUYNH — số tab Khách hàng báo.
        /// Phần còn lại (<c>PlatformKept − ParentFeeKept</c>) là phí gia sư, số tab Gia sư báo.
        /// </summary>
        decimal ParentFeeKept,
        /// <summary>Lát của <see cref="Adjustment"/> thuộc vế phí phụ huynh.</summary>
        decimal ParentFeeAdjustment,
        /// <summary>Lát của <see cref="Adjustment"/> thuộc vế phí gia sư. Hai lát cộng
        /// đúng bằng <see cref="Adjustment"/>.</summary>
        decimal TutorCutAdjustment,
        /// <summary>Khác hẳn ba trường trên: đây là phần chênh của TIỀN GIA SƯ NHẬN
        /// (<c>Tutorfee</c>), không phải của phí sàn.</summary>
        decimal TutorAdjustment,
        int Delivered);

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
                b.Depositamount ?? 0,
                b.Escrowstatus,
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

    /// <summary>
    /// Tổng Refund và EscrowRelease đã ghi sổ cho từng booking.
    ///
    /// Lọc <c>Referencetable == booking</c> chứ không chỉ lọc theo loại giao dịch: ví còn có
    /// khoản <c>Refund</c> gắn <c>Referencetable == withdrawal</c> — tiền trả lại ví gia sư khi
    /// admin từ chối lệnh rút (<c>AdminPayoutService</c>), không liên quan gì tới học phí.
    /// </summary>
    private async Task<Dictionary<int, (decimal Refunded, decimal Released)>> LoadBookingLedgerAsync(
        CancellationToken ct)
    {
        var rows = await context.Wallettransactions.AsNoTracking()
            .Where(t => t.Referencetable == ReferenceTable.Booking
                        && t.Referenceid != null
                        && (t.Transactiontype == TransactionType.Refund
                            || t.Transactiontype == TransactionType.EscrowRelease))
            .Select(t => new { BookingId = t.Referenceid!.Value, t.Transactiontype, Amount = t.Amount ?? 0 })
            .ToListAsync(ct);

        return rows
            .GroupBy(r => r.BookingId)
            .ToDictionary(
                g => g.Key,
                g => (
                    Refunded: g.Where(r => r.Transactiontype == TransactionType.Refund).Sum(r => r.Amount),
                    Released: g.Where(r => r.Transactiontype == TransactionType.EscrowRelease).Sum(r => r.Amount)));
    }

    /// <summary>
    /// Tiền mặt phụ huynh đã thực trả vào một booking. Cùng công thức với
    /// <c>SettlementService.CancelRemainingSessionsAsync</c> để sổ ví và báo cáo không kể hai
    /// câu chuyện khác nhau về cùng một booking.
    /// </summary>
    private static decimal CashPaidIn(BookingFlat b) =>
        b.RemainingPaidAt != null ? b.FinalPrice
        : b.DepositPaidAt != null ? b.DepositAmount
        : 0m;

    /// <summary>
    /// Mọi booking đã chốt sổ — xem <see cref="ClosedBooking"/>.
    ///
    /// Gồm cả booking chốt sổ mà KHÔNG có tiền (gia sư từ chối, quá hạn thanh toán): chúng ra
    /// <c>PlatformKept = 0</c>, nên nếu dữ liệu có buổi settle lạc vào một booking như thế thì
    /// <c>Adjustment</c> âm sẽ khử đúng phần hoa hồng ảo đó. Chỗ loại chúng ra là
    /// <see cref="CohortBookings"/> (lọc <c>CashIn > 0</c>), vì đưa vào GMV mới là thổi phồng.
    /// </summary>
    private static List<ClosedBooking> BuildClosedBookings(
        IEnumerable<BookingFlat> bookings,
        IEnumerable<SessionFlat> sessions,
        IReadOnlyDictionary<int, (decimal Refunded, decimal Released)> ledger)
    {
        var deliveries = BuildDeliveries(sessions);

        var closed = new List<ClosedBooking>();
        foreach (var b in bookings)
        {
            if (!IsBooksClosed(b)) continue;

            var cashIn = CashPaidIn(b);
            var (refunded, released) = ledger.TryGetValue(b.BookingId, out var l) ? l : (0m, 0m);
            var delivered = DeliveryOf(deliveries, b.BookingId);

            // Chặn trên ở PlatformFee là điều kiện an toàn của cả thay đổi này: dù sổ ví thiếu
            // dòng (dữ liệu dev sửa tay), doanh thu báo ra vẫn KHÔNG BAO GIỜ vượt quá hoa hồng
            // đã ký — đúng bằng trần mà công thức cũ có thể cho ra. Sai sót chỉ có thể theo
            // hướng thiếu, không thể theo hướng thừa.
            var kept = Math.Clamp(cashIn - refunded - released, 0, Math.Max(b.PlatformFee, 0));

            // Phần doanh thu đã chín TRƯỚC lúc chốt sổ, theo đúng hai mốc của RecognisedIn —
            // phải cùng công thức, nếu không hai nửa sẽ không cộng ra PlatformKept.
            // Hai vế đã chín trước lúc chốt sổ, tách riêng vì mỗi tab báo cáo một vế.
            var parentEarned = ParentFeeEarned(b, DateTime.MaxValue, delivered);
            var tutorEarned = TutorCutPerSession(b)
                              * Math.Min(Math.Max(delivered.Count, 0), b.TotalSessions);
            var earnedBeforeClosing = parentEarned + tutorEarned;
            var adjustment = kept - earnedBeforeClosing;

            // ─── Chia phần chênh khi chốt sổ về hai nguồn ────────────────────────────
            //
            // `adjustment` là phần sổ ví nói khác công thức — hoàn tiền một phần theo khiếu
            // nại, hoặc dữ liệu sửa tay. Nó không mang sẵn thông tin "khoản này của bên nào",
            // nên chia theo TỈ LỆ hai vế đã chín: bên nào đóng góp nhiều hơn thì chịu/hưởng
            // phần chênh nhiều hơn. Vế thứ hai lấy bằng HIỆU để hai mảnh luôn cộng đúng bằng
            // `adjustment`, không sinh ra chênh lệch làm tròn.
            //
            // Khi chưa vế nào chín mà ví vẫn báo giữ được tiền, dồn hết về vế PHỤ HUYNH: đó là
            // tiền của phụ huynh mà hệ thống không hoàn, không liên quan gì tới công gia sư.
            decimal parentAdjustment;
            if (earnedBeforeClosing > 0)
                parentAdjustment = Math.Round(adjustment * (parentEarned / earnedBeforeClosing), 2);
            else
                parentAdjustment = adjustment;

            var tutorPerSession = b.TotalSessions > 0
                ? Math.Round(b.TutorFee / b.TotalSessions, 2)
                : 0m;

            closed.Add(new ClosedBooking(
                BookingId: b.BookingId,
                TutorId: b.TutorId,
                SubjectId: b.SubjectId,
                GradeId: b.GradeId,
                CustomerKey: b.ParentId ?? b.StudentId,
                // Huỷ thì có Cancelledat. Hoàn tất thì không có cột nào ghi ngày đóng, nên lấy
                // ngày buổi cuối được settle — sát thực tế hơn Updatedat (đổi theo mọi lần sửa)
                // và luôn nằm trong khoảng hoạt động của khoá.
                When: b.CancelledAt ?? delivered.Last ?? b.CreatedAt ?? DateTime.MinValue,
                Cancelled: !IsLive(b),
                CashIn: cashIn,
                Refunded: refunded,
                TutorPaid: released,
                PlatformKept: kept,
                Adjustment: adjustment,
                ParentFeeKept: parentEarned + parentAdjustment,
                ParentFeeAdjustment: parentAdjustment,
                TutorCutAdjustment: adjustment - parentAdjustment,
                TutorAdjustment: released - tutorPerSession * delivered.Count,
                Delivered: delivered.Count));
        }
        return closed;
    }

    /// <summary>
    /// Phần doanh thu chốt thêm (hoặc trả lại) tại thời điểm đóng sổ, của các booking đóng sổ
    /// TRONG kỳ. Luôn đi kèm <see cref="RecognisedIn"/>; một mình nó không phải doanh thu.
    /// </summary>
    private static decimal ClosingAdjustmentIn(
        IEnumerable<ClosedBooking> closed, DateTime fromUtc, DateTime toUtc) =>
        closed.Where(c => c.When >= fromUtc && c.When < toUtc).Sum(c => c.Adjustment);

    // ─── Hai vế của phí sàn, hai mốc chín khác nhau ────────────────────────────────────
    //
    // Tiền của một khoá nằm trong ESCROW, không phải trong túi Tutora. Nên phí sàn 10% không
    // chín cùng một lúc — nó gồm hai khoản mà điều kiện "hết đường hoàn" khác hẳn nhau:
    //
    //   • PHÍ GIA SƯ (5% cắt từ `Tutorfee`) chín theo TỪNG BUỔI dạy xong. Buổi chưa dạy thì
    //     escrow bị ĐẢO — gia sư không nhận, nên Tutora cũng không có gì để cắt.
    //
    //   • PHÍ PHỤ HUYNH (5% thu thêm, `Parentfee`) chín khi BUỔI ĐẦU TIÊN đã dạy xong — KHÔNG
    //     phải lúc thanh toán. Trước buổi đầu, phụ huynh huỷ được và nhận lại 100% kể cả phí:
    //     `BookingService.CancelBooking` hoàn trọn `Depositamount` qua
    //     `TutorResponseTimeoutPolicy.ParentRefundAmount`, và `HasStartedOrSettledLesson` chặn
    //     đúng luồng đó lại ngay khi có một buổi settle. Sau mốc ấy khoản phí mới hết đường về:
    //     huỷ giữa chừng chỉ hoàn GIÁ GỐC của buổi chưa dạy
    //     (`LessonRefundCalculator.ParentRefundPerSessionNoFee`).
    //
    // Nên mốc ghi nhận một lát phí phụ huynh = MUỘN HƠN giữa (ngày trả đợt đó, ngày buổi đầu
    // dạy xong). Trước đó nó là doanh thu TẠM TÍNH, không phải tiền thật.
    //
    // Công thức cũ `FeePerSession = PlatformFee / TotalSessions` gộp cả hai vào ngày dạy; bản
    // trước đó của file này thì cho phí phụ huynh chín ngay lúc thanh toán — cả hai đều sai.
    // Tổng hai vế vẫn đúng bằng `PlatformFee` khi khoá chạy trọn vẹn.

    /// <summary>Buổi đã settle của một booking: số buổi + mốc đầu/cuối.</summary>
    private sealed record Delivery(int Count, DateTime? First, DateTime? Last)
    {
        public static readonly Delivery None = new(0, null, null);
    }

    /// <summary>
    /// Gom buổi đã settle theo booking. <paramref name="asOf"/> cắt theo thời điểm — cần cho
    /// những chỉ tiêu luỹ kế "tính tới cuối kỳ" như nợ dịch vụ.
    /// </summary>
    private static Dictionary<int, Delivery> BuildDeliveries(
        IEnumerable<SessionFlat> sessions, DateTime? asOf = null) =>
        sessions
            .Where(s => s.Settled && (asOf == null || s.When < asOf))
            .GroupBy(s => s.BookingId)
            .ToDictionary(
                g => g.Key,
                g => new Delivery(g.Count(), g.Min(x => x.When), g.Max(x => x.When)));

    private static Delivery DeliveryOf(IReadOnlyDictionary<int, Delivery> map, int bookingId) =>
        map.TryGetValue(bookingId, out var d) ? d : Delivery.None;

    /// <summary>5% phụ huynh trả thêm, phân bổ cho một buổi. Đợt cọc mua đúng một buổi.</summary>
    private static decimal ParentFeePerSession(BookingFlat b) =>
        b.TotalSessions <= 0 ? 0 : Math.Round(b.ParentFee / b.TotalSessions, 2);

    /// <summary>
    /// 5% cắt từ tiền gia sư, phân bổ cho một buổi — chia đều như <c>SettlementService</c> chia
    /// escrow. Chỉ chín khi buổi đó dạy xong và settle.
    /// </summary>
    private static decimal TutorCutPerSession(BookingFlat b) =>
        b.TotalSessions <= 0 ? 0 : Math.Round((b.PlatformFee - b.ParentFee) / b.TotalSessions, 2);

    /// <summary>Lát cọc của phí phụ huynh. Phần còn lại lấy bằng HIỆU để trả đủ luôn cộng
    /// tròn <c>ParentFee</c> dù chia lẻ.</summary>
    private static decimal ParentFeeDepositSlice(BookingFlat b) =>
        Math.Min(ParentFeePerSession(b), b.ParentFee);

    /// <summary>
    /// Ngày một lát phí phụ huynh trở thành tiền thật: muộn hơn giữa ngày trả tiền đợt đó và
    /// ngày buổi đầu dạy xong. <c>null</c> khi chưa trả, hoặc chưa buổi nào được dạy — lúc đó
    /// khoản phí vẫn hoàn lại được 100%.
    /// </summary>
    private static DateTime? ParentFeeSliceMaturesAt(DateTime? paidAt, DateTime? firstDeliveredAt)
    {
        if (paidAt == null || firstDeliveredAt == null) return null;
        return paidAt.Value >= firstDeliveredAt.Value ? paidAt.Value : firstDeliveredAt.Value;
    }

    /// <summary>Phí phụ huynh đã chín tính tới <paramref name="asOf"/>.</summary>
    private static decimal ParentFeeEarned(BookingFlat b, DateTime asOf, Delivery d)
    {
        decimal total = 0;
        var deposit = ParentFeeDepositSlice(b);

        var depositAt = ParentFeeSliceMaturesAt(b.DepositPaidAt, d.First);
        if (depositAt != null && depositAt <= asOf) total += deposit;

        var remainingAt = ParentFeeSliceMaturesAt(b.RemainingPaidAt, d.First);
        if (remainingAt != null && remainingAt <= asOf) total += b.ParentFee - deposit;

        return total;
    }

    /// <summary>
    /// Lát phí phụ huynh chín TRONG kỳ. Dùng cho mọi biểu đồ/bảng cắt theo thời gian; các tab
    /// cắt theo chiều khác (gia sư, môn, khách hàng) gọi cùng hàm này rồi gom theo chiều của
    /// mình, nên các tab luôn cộng khớp.
    /// </summary>
    private static decimal ParentFeeRecognisedIn(
        BookingFlat b, DateTime fromUtc, DateTime toUtc, Delivery d)
    {
        decimal total = 0;
        var deposit = ParentFeeDepositSlice(b);

        var depositAt = ParentFeeSliceMaturesAt(b.DepositPaidAt, d.First);
        if (depositAt >= fromUtc && depositAt < toUtc) total += deposit;

        var remainingAt = ParentFeeSliceMaturesAt(b.RemainingPaidAt, d.First);
        if (remainingAt >= fromUtc && remainingAt < toUtc) total += b.ParentFee - deposit;

        return total;
    }

    /// <summary>
    /// Doanh thu đã chín của một booking CHƯA chốt sổ, luỹ kế tới <paramref name="asOf"/>:
    /// phí phụ huynh (nếu đã qua buổi đầu) + phí gia sư của các buổi đã dạy. Khoá đã chốt sổ
    /// KHÔNG dùng hàm này — tiền của chúng đọc thẳng từ sổ ví
    /// (<see cref="ClosedBooking.PlatformKept"/>).
    /// </summary>
    private static decimal EarnedSoFar(BookingFlat b, DateTime asOf, Delivery d) =>
        ParentFeeEarned(b, asOf, d) + TutorCutEarned(b, d);

    /// <summary>
    /// Vế PHÍ GIA SƯ đã chín — số mà tab Gia sư báo. Không phụ thuộc mốc thời gian nào ngoài
    /// chính các buổi đã settle, nên không nhận <c>asOf</c>: <see cref="BuildDeliveries"/> đã
    /// cắt sẵn theo kỳ ở nơi gọi.
    /// </summary>
    private static decimal TutorCutEarned(BookingFlat b, Delivery d) =>
        TutorCutPerSession(b) * Math.Min(Math.Max(d.Count, 0), b.TotalSessions);

    /// <summary>
    /// Vế PHÍ PHỤ HUYNH còn CHỜ chín — số "doanh thu đợi ghi nhận" của tab Khách hàng. Gồm cả
    /// đợt chưa trả lẫn đợt đã trả mà chưa qua buổi đầu (vẫn hoàn lại được 100%).
    /// </summary>
    private static decimal ParentFeePending(BookingFlat b, DateTime asOf, Delivery d) =>
        Math.Max(0, b.ParentFee - ParentFeeEarned(b, asOf, d));

    /// <summary>
    /// Phần phí sàn đã ký nhưng CHƯA chín — số "còn chờ" của vành khuyên và của nợ dịch vụ.
    /// Luôn bằng <c>PlatformFee − EarnedSoFar</c> nên hai lát cộng lại đúng bằng doanh thu
    /// tạm tính, không cần bộ lọc riêng nào.
    /// </summary>
    private static decimal UnearnedSoFar(BookingFlat b, DateTime asOf, Delivery d) =>
        Math.Max(0, b.PlatformFee - EarnedSoFar(b, asOf, d));

    /// <summary>
    /// Tập booking dùng cho mọi con số theo COHORT (GMV, hoa hồng đã ký, phân bổ tiền): booking
    /// đang chạy, CỘNG booking đã đóng sổ mà phụ huynh đã thực trả tiền.
    ///
    /// Nhóm thứ hai trước đây bị loại hoàn toàn, nên một khoá học phụ huynh đã trả 105.000đ và
    /// học một buổi lại hiện GMV = 0 ngay cạnh thẻ "Đã hoàn tiền 90.000đ" — hai con số tự mâu
    /// thuẫn trên cùng một hàng. Chúng vào cohort theo giá HỢP ĐỒNG (giống mọi booking khác),
    /// còn phần thực thu được kể riêng ở <c>CommissionEarned</c> / <c>CommissionLost</c>.
    /// </summary>
    private static List<BookingFlat> CohortBookings(
        IEnumerable<BookingFlat> bookings, IEnumerable<ClosedBooking> closed)
    {
        // Chỉ khoá đóng sổ mà phụ huynh THỰC SỰ còn mất tiền mới là giao dịch.
        //
        // Điều kiện là `CashIn − Refunded > 0`, không phải `CashIn > 0`. Hai nhóm bị loại vì
        // cùng một lý do:
        //
        //   • huỷ khi chưa ai trả đồng nào (gia sư không nhận, quá hạn thanh toán) — CashIn = 0;
        //   • ĐÃ TRẢ rồi được hoàn 100% (huỷ trước buổi đầu, gia sư không phản hồi, no-show) —
        //     phụ huynh lấy lại từng đồng, nên về mặt kinh tế giống hệt nhóm trên.
        //
        // Bản cũ chỉ chặn nhóm đầu. Nhóm thứ hai lọt vào cohort theo giá HỢP ĐỒNG, nên một khoá
        // phụ huynh trả 157.500đ rồi lấy về hết vẫn cộng 1.417.500đ vào "Tiền phụ huynh trả" và
        // 135.000đ vào "Doanh thu tạm tính" — rồi lập tức bị trừ lại ở lát "Không thu được".
        // Đo trên dữ liệu dev 01/09/2026: 8 khoá như thế thổi GMV lên 17,9% (21,4tr → 17,6tr)
        // và chiếm 63% lát "Không thu được" (647.500 → 237.500).
        //
        // Câu chuyện hoàn tiền KHÔNG bị giấu: thẻ "Đã hoàn tiền" (đếm theo sổ ví, không lọc
        // cohort) và thẻ "Booking dừng sau đợt 1" vẫn kể đủ.
        //
        // Khoá hoàn MỘT PHẦN thì ở lại: phụ huynh vẫn mất tiền thật, sàn vẫn giữ được phần phí
        // không hoàn — đó là giao dịch có thật và khoản chênh là khoản mất có thật.
        var closedIds = closed
            .Where(c => c.CashIn - c.Refunded > 0)
            .Select(c => c.BookingId)
            .ToHashSet();
        return bookings.Where(b => IsLive(b) || closedIds.Contains(b.BookingId)).ToList();
    }

    /// <summary>
    /// Buổi này có được tính hoa hồng không.
    ///
    /// MỌI booking, không phân biệt trạng thái: buổi đã dạy xong và đã settle là dịch vụ đã
    /// giao, hoa hồng của nó có thật và thuộc về đúng THÁNG DẠY. Trạng thái của booking về sau
    /// không xoá ngược được điều đó — nếu số tiền cuối cùng khác đi, phần chênh được chỉnh một
    /// lần tại ngày chốt sổ qua <see cref="ClosedBooking.Adjustment"/>.
    ///
    /// Một chỗ duy nhất định nghĩa quy tắc, để các tab không tự đặt ra bộ lọc riêng rồi ra
    /// những con số lệch nhau như trước.
    /// </summary>
    private static bool IsRevenueSession(SessionFlat s, Dictionary<int, BookingFlat> bookingById) =>
        bookingById.ContainsKey(s.BookingId);

    /// <summary>
    /// Doanh thu chín TRONG kỳ, gộp hai mốc tự nhiên của luồng escrow:
    ///
    ///   • phí phụ huynh của các đợt CHÍN trong kỳ — muộn hơn giữa ngày trả và ngày buổi đầu;
    ///   • phí gia sư của các buổi settle trong kỳ  — neo theo NGÀY DẠY.
    ///
    /// Đây mới là hai phần ba của "doanh thu ghi nhận": phần còn lại là
    /// <see cref="ClosingAdjustmentIn"/>, neo theo ngày chốt sổ. Ba mốc khác nhau nên phải tách
    /// hàm, nhưng <see cref="ClosingAdjustmentIn"/> luôn được cộng cùng hàm này ở mọi nơi báo
    /// doanh thu — một mình nó không phải doanh thu.
    /// </summary>
    private static decimal RecognisedIn(
        IEnumerable<SessionFlat> sessions,
        Dictionary<int, BookingFlat> bookingById,
        DateTime fromUtc,
        DateTime toUtc)
    {
        var sessionList = sessions as IList<SessionFlat> ?? sessions.ToList();
        var deliveries = BuildDeliveries(sessionList);

        return sessionList
            .Where(s => s.Settled && s.When >= fromUtc && s.When < toUtc)
            .Sum(s => bookingById.TryGetValue(s.BookingId, out var b) ? TutorCutPerSession(b) : 0)
            + bookingById.Values.Sum(b =>
                ParentFeeRecognisedIn(b, fromUtc, toUtc, DeliveryOf(deliveries, b.BookingId)));
    }

    /// <summary>Doanh thu ĐÃ KÝ trong kỳ: toàn bộ phí quy về ngày tạo booking.
    /// Nhận sẵn cohort (<see cref="CohortBookings"/>) nên gồm cả booking về sau đóng sổ —
    /// "nếu tính ngay lúc đặt lịch" thì lúc đó chưa ai biết booking sẽ bị huỷ.</summary>
    private static decimal ContractedIn(
        IEnumerable<BookingFlat> cohort,
        DateTime fromUtc,
        DateTime toUtc) =>
        cohort
            .Where(b => b.CreatedAt >= fromUtc && b.CreatedAt < toUtc)
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
