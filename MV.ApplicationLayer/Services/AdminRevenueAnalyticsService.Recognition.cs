using Microsoft.EntityFrameworkCore;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.ResponseModel.Admin;

namespace MV.ApplicationLayer.Services;

public partial class AdminRevenueAnalyticsService
{
    public async Task<AdminRevenueRecognitionResponse> GetRecognitionAsync(
        DateTime? from, DateTime? to, CancellationToken ct = default)
    {
        var (fromUtc, toUtc) = Normalise(from, to);
        var (prevFrom, prevTo) = PreviousPeriod(fromUtc, toUtc);

        var bookings = await LoadBookingsAsync(ct);
        var sessions = await LoadSessionsAsync(ct);
        var bookingById = bookings.ToDictionary(b => b.BookingId);
        var revenueBookings = bookings
            .Where(b => RevenueBookingStatuses.Contains(b.Status ?? ""))
            .ToList();

        var overview = await GetOverviewAsync(from, to, ct);

        var settledCount = sessions
            .Where(s => s.Settled)
            .GroupBy(s => s.BookingId)
            .ToDictionary(g => g.Key, g => g.Count());

        // Tuổi nợ của phần chưa thực hiện, tính từ ngày tạo booking.
        var now = toUtc;
        var buckets = new (string Label, int MinDays, int MaxDays)[]
        {
            ("0-30 ngày", 0, 30),
            ("31-60 ngày", 31, 60),
            ("61-90 ngày", 61, 90),
            ("> 90 ngày", 91, int.MaxValue),
        };

        var aging = buckets.Select(bk => new DeferredAgingDto
        {
            Bucket = bk.Label,
            Amount = 0,
            Bookings = 0,
        }).ToList();

        foreach (var b in revenueBookings)
        {
            if (b.CreatedAt == null) continue;
            var settled = settledCount.TryGetValue(b.BookingId, out var c) ? c : 0;
            var pending = Math.Max(b.TotalSessions - settled, 0);
            if (pending == 0) continue;

            var age = (int)(now - b.CreatedAt.Value).TotalDays;
            var idx = Array.FindIndex(buckets, bk => age >= bk.MinDays && age <= bk.MaxDays);
            if (idx < 0) idx = buckets.Length - 1;

            aging[idx].Amount += FeePerSession(b) * pending;
            aging[idx].Bookings += 1;
        }

        // Booking chết sau đợt 1: đã trả deposit, quá hạn trả đợt 2, chưa hoàn tất.
        // Xét toàn bộ booking: ca huỷ mang status cancelled, nằm ngoài revenueBookings.
        int SettledOf(BookingFlat b) => settledCount.TryGetValue(b.BookingId, out var c) ? c : 0;

        var stalledNow = bookings.Where(b => IsStalledAfterDeposit(b, now, SettledOf(b))).ToList();
        var stalledPrev = bookings.Where(b => IsStalledAfterDeposit(b, prevTo, SettledOf(b))).ToList();

        var neverStartedNow = bookings.Where(b => IsPaidButNeverStarted(b, now, SettledOf(b))).ToList();
        var neverStartedPrev = bookings
            .Where(b => IsPaidButNeverStarted(b, prevTo, SettledOf(b)))
            .ToList();

        // Mẫu số cùng tập với tử số, và chỉ gồm booking thực sự vượt được đợt 1.
        var depositCohortNow = bookings.Count(b =>
            HasPassedDeposit(b) && b.CreatedAt >= fromUtc && b.CreatedAt < toUtc);
        var depositCohortPrev = bookings.Count(b =>
            HasPassedDeposit(b) && b.CreatedAt >= prevFrom && b.CreatedAt < prevTo);

        var stalled = new StalledBookingStatsDto
        {
            Count = stalledNow.Count,
            CountPrevious = stalledPrev.Count,
            ContractedFeeAtRisk = stalledNow.Sum(b =>
            {
                var settled = settledCount.TryGetValue(b.BookingId, out var c) ? c : 0;
                return FeePerSession(b) * Math.Max(b.TotalSessions - settled, 0);
            }),
            DropOffRate = depositCohortNow == 0
                ? 0
                : Math.Round((decimal)stalledNow.Count / depositCohortNow * 100, 1),
            DropOffPrevious = depositCohortPrev == 0
                ? 0
                : Math.Round((decimal)stalledPrev.Count / depositCohortPrev * 100, 1),
        };

        var stalledTrend = new List<StalledTrendPointDto>();
        foreach (var ms in MonthBuckets(fromUtc, toUtc))
        {
            var me = ms.AddMonths(1);
            // Cohort lấy từ toàn bộ booking để gồm cả ca huỷ sau khi trả cọc.
            var cohort = bookings.Where(b => b.CreatedAt >= ms && b.CreatedAt < me).ToList();
            stalledTrend.Add(new StalledTrendPointDto
            {
                Month = MonthKey(ms),
                Stalled = cohort.Count(b => IsStalledAfterDeposit(b, now, SettledOf(b))),
                Converted = cohort.Count(b =>
                    b.Status is BookingStatus.Paid or BookingStatus.Ongoing or BookingStatus.Completed),
            });
        }

        // Hoàn tiền
        // Dùng wallet_transactions vì Booking.Refundamount chỉ là số luỹ kế, không có
        // mốc thời gian nên không quy được về kỳ.
        var refundTx = await context.Wallettransactions.AsNoTracking()
            .Where(t => t.Transactiontype == TransactionType.Refund)
            .Select(t => new { Amount = t.Amount ?? 0, t.Createdat, t.Referenceid })
            .ToListAsync(ct);

        var refundsInPeriod = refundTx
            .Where(t => t.Createdat >= fromUtc && t.Createdat < toUtc)
            .ToList();
        var refundsPrev = refundTx
            .Where(t => t.Createdat >= prevFrom && t.Createdat < prevTo)
            .ToList();

        var refunds = new RefundStatsDto
        {
            Amount = refundsInPeriod.Sum(t => t.Amount),
            AmountPrevious = refundsPrev.Sum(t => t.Amount),
            Count = refundsInPeriod.Count,
            CountPrevious = refundsPrev.Count,
            // Tỷ trọng trên tiền mặt đã thu trong kỳ — đo mức rò rỉ của dòng tiền vào.
            RateOfCash = overview.Summary.CashCollected == 0
                ? 0
                : Math.Round(refundsInPeriod.Sum(t => t.Amount) / overview.Summary.CashCollected * 100, 1),
        };

        var refundTrend = new List<RefundTrendPointDto>();
        foreach (var ms in MonthBuckets(fromUtc, toUtc))
        {
            var me = ms.AddMonths(1);
            var monthRefunds = refundTx.Where(t => t.Createdat >= ms && t.Createdat < me).ToList();
            refundTrend.Add(new RefundTrendPointDto
            {
                Month = MonthKey(ms),
                Amount = monthRefunds.Sum(t => t.Amount),
                Count = monthRefunds.Count,
            });
        }

        // Chi tiết booking chưa hoàn thành — lấy 30 booking chậm nhất.
        var parentNames = await context.Users.AsNoTracking()
            .Select(u => new { u.Userid, u.Fullname })
            .ToDictionaryAsync(u => u.Userid, u => u.Fullname ?? u.Userid, ct);
        // Query filter Deletedat == null loại mất gia sư xoá mềm → fallback sang users.
        var tutorNames = await context.Tutorprofiles.AsNoTracking()
            .Select(t => new { t.Tutorid, Name = t.Tutor != null ? t.Tutor.Fullname : null })
            .ToDictionaryAsync(t => t.Tutorid, t => t.Name ?? t.Tutorid, ct);

        var studentProfileNames = await context.Studentprofiles.AsNoTracking()
            .Select(s => new { s.Studentid, s.Fullname })
            .ToDictionaryAsync(s => s.Studentid, s => s.Fullname ?? s.Studentid, ct);

        // Tra users trước: học sinh tự đăng ký không có student_profiles.
        string PayerName(BookingFlat b)
        {
            var id = b.ParentId ?? b.StudentId;
            if (id == null) return "—";
            if (parentNames.TryGetValue(id, out var un)) return un;
            if (studentProfileNames.TryGetValue(id, out var sn)) return sn;
            return id;
        }
        var subjectNames = await context.Subjects.AsNoTracking()
            .ToDictionaryAsync(s => s.Subjectid, s => s.Subjectname, ct);

        // Trước đây chỉ liệt kê booking CHƯA hoàn thành và cắt còn 30 dòng (bảng chẩn đoán
        // "ghi nhận sớm vs thực hiện"). Nay đổi thành danh sách booking đầy đủ kèm chỉ số doanh
        // thu để admin tra cứu trực tiếp — booking đã hoàn thành mới là nguồn doanh thu chắc
        // chắn nhất, không có lý do gì giấu đi. Vẫn giữ phạm vi `revenueBookings` (chỉ các
        // trạng thái thực sự phát sinh doanh thu) để tổng ở bảng khớp với phần còn lại của tab.
        var progress = revenueBookings
            .Select(b =>
            {
                var settled = settledCount.TryGetValue(b.BookingId, out var c) ? c : 0;
                return new BookingProgressDto
                {
                    BookingId = b.BookingId,
                    ParentName = PayerName(b),
                    TutorName = b.TutorId == null
                        ? "—"
                        : tutorNames.TryGetValue(b.TutorId, out var tn) ? tn
                        : parentNames.TryGetValue(b.TutorId, out var tu) ? tu
                        : b.TutorId,
                    Subject = b.SubjectId.HasValue && subjectNames.TryGetValue(b.SubjectId.Value, out var sn) ? sn : "—",
                    TotalSessions = b.TotalSessions,
                    DeliveredSessions = settled,
                    ContractedFee = b.PlatformFee,
                    RecognisedFee = FeePerSession(b) * settled,
                    CreatedAt = b.CreatedAt,
                    Status = b.Status ?? "",
                };
            })
            // Mới nhất lên đầu — hợp với việc tra cứu hơn là sắp theo tiến độ như bảng chẩn đoán cũ.
            .OrderByDescending(p => p.CreatedAt ?? DateTime.MinValue)
            .ThenByDescending(p => p.BookingId)
            .ToList();

        return new AdminRevenueRecognitionResponse
        {
            Summary = overview.Summary,
            DeferredAging = aging,
            Stalled = stalled,
            NeverStarted = new NeverStartedStatsDto
            {
                Count = neverStartedNow.Count,
                CountPrevious = neverStartedPrev.Count,
                FeeAtRisk = neverStartedNow.Sum(b => FeePerSession(b) * b.TotalSessions),
                CashHeld = neverStartedNow.Sum(b => b.FinalPrice),
            },
            StalledTrend = stalledTrend,
            Refunds = refunds,
            RefundTrend = refundTrend,
            BookingProgress = progress,
        };
    }
}
