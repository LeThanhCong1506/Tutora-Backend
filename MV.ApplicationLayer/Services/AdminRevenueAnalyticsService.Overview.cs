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

        var bookingById = bookings.ToDictionary(b => b.BookingId);
        var revenueBookings = bookings
            .Where(b => RevenueBookingStatuses.Contains(b.Status ?? ""))
            .ToList();

        var aiIn = (DateTime f, DateTime t) =>
            aiPayments.Where(p => p.When >= f && p.When < t).Sum(p => p.Amount);

        var recognised = RecognisedIn(sessions, bookingById, fromUtc, toUtc) + aiIn(fromUtc, toUtc);
        var recognisedPrev = RecognisedIn(sessions, bookingById, prevFrom, prevTo) + aiIn(prevFrom, prevTo);
        var contracted = ContractedIn(bookings, fromUtc, toUtc) + aiIn(fromUtc, toUtc);
        var contractedPrev = ContractedIn(bookings, prevFrom, prevTo) + aiIn(prevFrom, prevTo);

        // Deferred = phí của booking đang chạy nhưng buổi CHƯA settle.
        var deferred = ComputeDeferred(revenueBookings, sessions, toUtc);
        var deferredPrev = ComputeDeferred(revenueBookings, sessions, prevTo);

        var gmv = revenueBookings
            .Where(b => b.CreatedAt >= fromUtc && b.CreatedAt < toUtc)
            .Sum(b => b.FinalPrice);
        var gmvPrev = revenueBookings
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
        var soldInPeriod = revenueBookings
            .Where(b => b.CreatedAt >= fromUtc && b.CreatedAt < toUtc)
            .ToList();
        var soldIds = soldInPeriod.Select(b => b.BookingId).ToHashSet();

        var settledByBooking = sessions
            .Where(x => x.Settled && x.When < toUtc)
            .GroupBy(x => x.BookingId)
            .ToDictionary(g => g.Key, g => g.Count());

        var commissionSold = soldInPeriod.Sum(b => b.PlatformFee);
        var baseAmount = soldInPeriod.Sum(b => b.FinalPrice - b.ParentFee);
        var tutorReceivable = soldInPeriod.Sum(b => b.TutorFee);
        var commissionEarned = soldInPeriod.Sum(b =>
            FeePerSession(b) * Math.Min(
                settledByBooking.TryGetValue(b.BookingId, out var c) ? c : 0,
                b.TotalSessions));

        // Buổi đã giải ngân của booking KHÔNG còn trạng thái doanh thu (đã hủy, hủy do khiếu
        // nại, staff hủy…). Hoa hồng này đã kiếm được thật — buổi đã dạy, tiền đã chuyển cho
        // gia sư — nên không xoá đi được, nhưng nó nằm ngoài CommissionSold.
        var commissionFromCancelled = sessions
            .Where(x => x.Settled && x.When >= fromUtc && x.When < toUtc
                        && !soldIds.Contains(x.BookingId))
            .Sum(x => bookingById.TryGetValue(x.BookingId, out var b)
                      && !RevenueBookingStatuses.Contains(b.Status ?? "")
                ? FeePerSession(b)
                : 0);

        var summary = new RevenueSummaryDto
        {
            BaseAmount = baseAmount,
            TutorReceivable = tutorReceivable,
            CommissionSold = commissionSold,
            CommissionEarned = commissionEarned,
            CommissionFromCancelled = commissionFromCancelled,
            RecognisedRevenue = recognised,
            RecognisedPrevious = recognisedPrev,
            ContractedRevenue = contracted,
            ContractedPrevious = contractedPrev,
            DeferredRevenue = deferred,
            DeferredPrevious = deferredPrev,
            Gmv = gmv,
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
                Recognised = RecognisedIn(sessions, bookingById, monthStart, monthEnd),
                Contracted = ContractedIn(bookings, monthStart, monthEnd),
                AiRevenue = aiIn(monthStart, monthEnd),
                Gmv = revenueBookings
                    .Where(b => b.CreatedAt >= monthStart && b.CreatedAt < monthEnd)
                    .Sum(b => b.FinalPrice),
            });
        }

        var bookingRevenueInPeriod = RecognisedIn(sessions, bookingById, fromUtc, toUtc);
        var mix = new List<NamedValueDto>
        {
            new() { Name = "Hoa hồng booking", Value = bookingRevenueInPeriod },
            new() { Name = "Gói AI credit", Value = aiIn(fromUtc, toUtc) },
        };

        var inPeriod = bookings
            .Where(b => b.CreatedAt >= fromUtc && b.CreatedAt < toUtc)
            .ToList();
        var funnel = new List<FunnelStepDto>
        {
            Step(BookingStatus.PendingTutor, "Chờ gia sư nhận", inPeriod),
            Step(BookingStatus.Accepted, "Gia sư đã nhận", inPeriod),
            Step(BookingStatus.DepositPaid, "Đã trả đợt 1", inPeriod),
            Step(BookingStatus.Paid, "Đã trả đủ", inPeriod),
            Step(BookingStatus.Ongoing, "Đang học", inPeriod),
            Step(BookingStatus.Completed, "Hoàn tất", inPeriod),
        };

        logger.LogInformation(
            "RevenueOverview {From:d}→{To:d}: recognised={Rec} contracted={Con} deferred={Def}",
            fromUtc, toUtc, recognised, contracted, deferred);

        return new AdminRevenueOverviewResponse
        {
            Summary = summary,
            Trend = trend,
            RevenueMix = mix,
            BookingFunnel = funnel,
        };
    }

    /// <summary>Phễu tích luỹ: đếm booking đã ĐẠT bậc đó trở lên, nên luôn giảm dần.</summary>
    private static FunnelStepDto Step(string stage, string label, List<BookingFlat> pool)
    {
        var rank = StageRank(stage);
        return new FunnelStepDto
        {
            Stage = stage,
            Label = label,
            Count = pool.Count(b => StageRank(b.Status ?? "") >= rank),
        };
    }

    private static int StageRank(string status) => status switch
    {
        BookingStatus.PendingTutor => 1,
        BookingStatus.Accepted or BookingStatus.PendingPayment => 2,
        BookingStatus.DepositPaid or BookingStatus.PendingRemainingPayment => 3,
        BookingStatus.Paid => 4,
        BookingStatus.Ongoing => 5,
        BookingStatus.Completed => 6,
        _ => 0,   // cancelled / payment_timeout: rơi khỏi phễu
    };

    /// <summary>Phí của buổi chưa settle, LUỸ KẾ tới asOf (không giới hạn kỳ) nên
    /// Deferred ≠ Contracted − Recognised — ba chỉ tiêu ba tập booking khác nhau.</summary>
    private static decimal ComputeDeferred(
        List<BookingFlat> revenueBookings,
        List<SessionFlat> sessions,
        DateTime asOf)
    {
        var settledByBooking = sessions
            .Where(s => s.Settled && s.When < asOf)
            .GroupBy(s => s.BookingId)
            .ToDictionary(g => g.Key, g => g.Count());

        decimal total = 0;
        foreach (var b in revenueBookings)
        {
            if (b.CreatedAt >= asOf) continue;
            var settled = settledByBooking.TryGetValue(b.BookingId, out var c) ? c : 0;
            var pending = Math.Max(b.TotalSessions - settled, 0);
            total += FeePerSession(b) * pending;
        }
        return total;
    }
}
