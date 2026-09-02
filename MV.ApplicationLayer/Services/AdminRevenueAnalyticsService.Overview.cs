using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.ResponseModel.Admin;
using MV.DomainLayer.Helpers;

namespace MV.ApplicationLayer.Services;

public partial class AdminRevenueAnalyticsService
{
    public async Task<AdminRevenueOverviewResponse> GetOverviewAsync(
        DateTime? from, DateTime? to, CancellationToken ct = default)
    {
        var (fromUtc, toUtc) = Normalise(from, to);
        var (prevFrom, prevTo) = PreviousPeriod(fromUtc, toUtc);

        var bookings = await LoadBookingsAsync(ct);
        var sessions = await LoadSessionsAsync(ct);
        var aiPayments = await LoadAiPaymentsAsync(ct);
        var ledger = await LoadBookingLedgerAsync(ct);

        var bookingById = bookings.ToDictionary(b => b.BookingId);
        // Booking chưa chốt sổ — dùng cho nợ dịch vụ, vì chỉ nhóm này mới còn buổi phải dạy.
        // Cùng tập với BuildClosedBookings — hai bên phải nhất trí booking nào đã chốt sổ.
        var nothingLeft = NothingLeftToTeach(sessions);
        var openBookings = bookings.Where(x => IsOpen(x, nothingLeft)).ToList();
        // Booking đã chốt sổ: tiền của chúng lấy từ sổ ví, không suy từ công thức.
        var closed = BuildClosedBookings(bookings, sessions, ledger);
        var cohort = CohortBookings(bookings, closed);

        var aiIn = (DateTime f, DateTime t) =>
            aiPayments.Where(p => p.When >= f && p.When < t).Sum(p => p.Amount);

        // Doanh thu ghi nhận = hoa hồng của mọi buổi đã dạy trong kỳ (neo theo ngày dạy)
        //                    + phần chênh chốt tại ngày đóng sổ của booking đóng trong kỳ
        //                    + bán gói AI.
        var recognised = RecognisedIn(sessions, bookingById, fromUtc, toUtc)
                         + ClosingAdjustmentIn(closed, fromUtc, toUtc) + aiIn(fromUtc, toUtc);
        var recognisedPrev = RecognisedIn(sessions, bookingById, prevFrom, prevTo)
                             + ClosingAdjustmentIn(closed, prevFrom, prevTo) + aiIn(prevFrom, prevTo);
        var contracted = ContractedIn(cohort, fromUtc, toUtc) + aiIn(fromUtc, toUtc);
        var contractedPrev = ContractedIn(cohort, prevFrom, prevTo) + aiIn(prevFrom, prevTo);

        // Deferred = phí của booking chưa chốt sổ mà buổi CHƯA settle. Booking đã chốt KHÔNG vào
        // đây: buổi chưa dạy của chúng đã bị huỷ, không còn là nghĩa vụ nào cả. Trước đây mọi
        // booking `completed` đều lọt vào, nên khoá kết thúc sớm (dạy 1/10 buổi) vẫn báo 9 buổi
        // nợ dịch vụ suốt đời — số đó không bao giờ thành tiền được nữa.
        var deferred = ComputeDeferred(openBookings, sessions, toUtc);
        var deferredPrev = ComputeDeferred(openBookings, sessions, prevTo);

        var gmv = cohort
            .Where(b => b.CreatedAt >= fromUtc && b.CreatedAt < toUtc)
            .Sum(b => b.FinalPrice);
        var gmvPrev = cohort
            .Where(b => b.CreatedAt >= prevFrom && b.CreatedAt < prevTo)
            .Sum(b => b.FinalPrice);

        var cashRows = await context.PaymentTransactions
            .AsNoTracking()
            .Where(t => t.Status == PaymentTransactionStatus.Succeeded
                        && t.Direction == PaymentTransactionDirection.Inbound)
            .Select(t => new { t.Amount, t.Paidat, t.Createdat })
            .ToListAsync(ct);
        var cash = cashRows
            .Select(r => (r.Amount, When: r.Paidat ?? r.Createdat ?? TimeZoneHelper.UtcNow))
            .ToList();

        // Bộ số cho khối chia tiền. Tất cả bám đúng MỘT tập: booking phát sinh doanh thu
        // được tạo trong kỳ. Cùng tập thì mới cộng khớp, và đó là toàn bộ mục đích của khối
        // này — người đọc cộng nhẩm ra đúng số thì không phải đi tìm lời giải thích nữa.
        var soldInPeriod = cohort
            .Where(b => b.CreatedAt >= fromUtc && b.CreatedAt < toUtc)
            .ToList();

        var deliveries = BuildDeliveries(sessions, toUtc);

        // Tiền Tutora thực giữ của booking đã đóng sổ, tra theo id — dùng ở cả CommissionEarned
        // lẫn CommissionLost bên dưới.
        var keptByBooking = closed.ToDictionary(c => c.BookingId, c => c.PlatformKept);

        var commissionSold = soldInPeriod.Sum(b => b.PlatformFee);
        // Cùng phép tính cho kỳ trước — ContractedIn chính là "phí của cohort tạo trong kỳ",
        // đúng định nghĩa của commissionSold, chỉ khác khoảng thời gian.
        var commissionSoldPrev = ContractedIn(cohort, prevFrom, prevTo);
        // Tiền khách ĐÃ THỰC trả trên đúng tập booking đã tính `gmv` — hai số phải cùng phạm vi
        // thì đặt cạnh nhau mới có nghĩa. Xem RevenueSummaryDto.GmvPaid.
        var gmvPaid = soldInPeriod.Sum(CashPaidIn);
        var baseAmount = soldInPeriod.Sum(b => b.FinalPrice - b.ParentFee);
        var tutorReceivable = soldInPeriod.Sum(b => b.TutorFee);

        // Booking chưa chốt sổ: phí phụ huynh đã trả + phí gia sư của buổi đã dạy (xem
        // EarnedSoFar — hai vế chín ở hai thời điểm khác nhau vì tiền nằm trong escrow).
        // Booking đã chốt: KHÔNG dùng công thức nữa — khoá dừng giữa chừng thì phần Tutora giữ
        // lại còn gồm cả phí dịch vụ không hoàn của những buổi bị huỷ. Lấy thẳng số đã chốt
        // trong ví. Với khoá hoàn tất bình thường hai cách cho ra CÙNG một số, nên nhánh này
        // không làm đổi nhóm đa số.
        var commissionEarned = soldInPeriod.Sum(b => keptByBooking.TryGetValue(b.BookingId, out var kept)
            ? kept
            : EarnedSoFar(b, toUtc, DeliveryOf(deliveries, b.BookingId)));

        // Hoa hồng đã ký nhưng vĩnh viễn không thu được: khoá bị huỷ, hoặc khách bỏ dở sau đợt 1
        // rồi hệ thống đóng khoá. Tách khỏi "chờ ghi nhận" vì chờ thì còn cơ hội thành tiền,
        // khoản này thì hết.
        var commissionLost = soldInPeriod
            .Where(b => keptByBooking.ContainsKey(b.BookingId))
            .Sum(b => Math.Max(0, b.PlatformFee - keptByBooking[b.BookingId]));

        // ── Ba số phận của doanh thu tạm tính, tính bằng CÔNG THỨC ─────────────────────
        //
        // Khác hẳn cặp commissionEarned/commissionLost ngay trên: bộ ba này KHÔNG đọc sổ ví một
        // dòng nào, chỉ dùng `EarnedSoFar` (phí phụ huynh đã chín + phí gia sư của buổi đã dạy).
        // Nhờ vậy nó miễn nhiễm với lỗi đảo escrow đang làm `PlatformKept` sai.
        //
        // Ba số cộng đúng bằng `commissionSold` theo construction: matured + (sold − matured)
        // tách làm hai nhánh tuỳ khoá đã chốt sổ hay chưa. Không có đường nào để chúng lệch.
        var maturedOf = (BookingFlat b) => EarnedSoFar(b, toUtc, DeliveryOf(deliveries, b.BookingId));
        var commissionMatured = soldInPeriod.Sum(maturedOf);
        var commissionPending = soldInPeriod
            .Where(b => !keptByBooking.ContainsKey(b.BookingId))
            .Sum(b => Math.Max(0, b.PlatformFee - maturedOf(b)));
        var commissionUnrecoverable = soldInPeriod
            .Where(b => keptByBooking.ContainsKey(b.BookingId))
            .Sum(b => Math.Max(0, b.PlatformFee - maturedOf(b)));

        // Đối soát với sổ ví: tổng Tutora giữ được từ các khoá bị HUỶ đóng sổ trong kỳ.
        // KHÔNG phải số hạng của RecognisedRevenue — một phần khoản này có thể đã được ghi nhận
        // ở kỳ trước dưới dạng hoa hồng của buổi đã dạy. Xem doc trên DTO.
        var commissionFromCancelled = closed
            .Where(c => c.Cancelled && c.When >= fromUtc && c.When < toUtc)
            .Sum(c => c.PlatformKept);

        var summary = new RevenueSummaryDto
        {
            BaseAmount = baseAmount,
            TutorReceivable = tutorReceivable,
            CommissionSold = commissionSold,
            CommissionSoldPrevious = commissionSoldPrev,
            CommissionEarned = commissionEarned,
            CommissionLost = commissionLost,
            CommissionMatured = commissionMatured,
            CommissionPending = commissionPending,
            CommissionUnrecoverable = commissionUnrecoverable,
            CommissionFromCancelled = commissionFromCancelled,
            RecognisedRevenue = recognised,
            RecognisedPrevious = recognisedPrev,
            ContractedRevenue = contracted,
            ContractedPrevious = contractedPrev,
            DeferredRevenue = deferred,
            DeferredPrevious = deferredPrev,
            Gmv = gmv,
            GmvPaid = gmvPaid,
            GmvPrevious = gmvPrev,
            CashCollected = cash.Where(c => c.When >= fromUtc && c.When < toUtc).Sum(c => c.Amount),
            CashPrevious = cash.Where(c => c.When >= prevFrom && c.When < prevTo).Sum(c => c.Amount),
        };

        // Xu hướng theo từng tháng trong khoảng đang xem
        var trend = new List<RevenueTrendPointDto>();
        foreach (var (monthStart, monthEnd, label) in TimeBuckets(fromUtc, toUtc))
        {
            trend.Add(new RevenueTrendPointDto
            {
                Month = label,
                Recognised = RecognisedIn(sessions, bookingById, monthStart, monthEnd)
                             + ClosingAdjustmentIn(closed, monthStart, monthEnd),
                Contracted = ContractedIn(cohort, monthStart, monthEnd),
                AiRevenue = aiIn(monthStart, monthEnd),
                Gmv = cohort
                    .Where(b => b.CreatedAt >= monthStart && b.CreatedAt < monthEnd)
                    .Sum(b => b.FinalPrice),
            });
        }

        // ─── RevenueMix và BookingFunnel đã BỎ khỏi response (31/08/2026) ────────────
        //
        // Cả hai được tính ở mỗi lần gọi nhưng không có màn hình nào đọc: giao diện từng vẽ
        // chúng ở hai khối "Cơ cấu doanh thu kỳ này" và "Phễu chuyển đổi booking", cả hai đã
        // bị gỡ khi gộp hai tab cũ thành tab Doanh thu (xem đầu file RevenueTab.tsx). Phần
        // tính toán thì bị bỏ quên lại.
        //
        // Đáng bỏ hơn bình thường vì từ 31/08 endpoint này còn được AdminDashboardService gọi
        // ở mỗi lần mở trang chủ admin, nên phần chết chạy gấp đôi số lần trước đây.
        //
        // Muốn dựng lại phễu thì đó là câu hỏi về BÁN HÀNG, không phải doanh thu — nên thuộc
        // về dashboard chứ không phải endpoint này.

        logger.LogInformation(
            "RevenueOverview {From:d}→{To:d}: recognised={Rec} contracted={Con} deferred={Def}",
            fromUtc, toUtc, recognised, contracted, deferred);

        return new AdminRevenueOverviewResponse
        {
            Summary = summary,
            Trend = trend,
        };
    }

    /// <summary>
    /// Phần phí sàn CHƯA chín, LUỸ KẾ tới asOf (không giới hạn kỳ) nên
    /// Deferred ≠ Contracted − Recognised — ba chỉ tiêu ba tập booking khác nhau.
    ///
    /// Đúng phần bù của <see cref="EarnedSoFar"/>: phí phụ huynh chưa chín (chưa trả, HOẶC đã
    /// trả nhưng chưa qua buổi đầu nên vẫn hoàn lại được 100%), cộng phí gia sư của các buổi
    /// CHƯA dạy.
    /// </summary>
    private static decimal ComputeDeferred(
        List<BookingFlat> revenueBookings,
        List<SessionFlat> sessions,
        DateTime asOf)
    {
        var deliveries = BuildDeliveries(sessions, asOf);

        decimal total = 0;
        foreach (var b in revenueBookings)
        {
            if (b.CreatedAt >= asOf) continue;
            total += UnearnedSoFar(b, asOf, DeliveryOf(deliveries, b.BookingId));
        }
        return total;
    }
}
