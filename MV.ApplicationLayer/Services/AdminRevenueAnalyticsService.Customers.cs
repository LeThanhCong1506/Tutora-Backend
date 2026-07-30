using Microsoft.EntityFrameworkCore;
using MV.DomainLayer.DTO.ResponseModel.Admin;

namespace MV.ApplicationLayer.Services;

public partial class AdminRevenueAnalyticsService
{
    public async Task<AdminCustomerRevenueResponse> GetCustomerRevenueAsync(
        DateTime? from, DateTime? to, int top, CancellationToken ct = default)
    {
        var (fromUtc, toUtc) = Normalise(from, to);
        var (prevFrom, prevTo) = PreviousPeriod(fromUtc, toUtc);

        var bookings = await LoadBookingsAsync(ct);
        var sessions = await LoadSessionsAsync(ct);
        var bookingById = bookings.ToDictionary(b => b.BookingId);

        // Học sinh tự đặt lịch có Parentid = null (BookingService.cs:127) — lọc
        // ParentId != null sẽ làm rơi cả nhóm này khỏi báo cáo.
        var revenueBookings = bookings
            .Where(b => RevenueBookingStatuses.Contains(b.Status ?? "")
                        && (b.ParentId != null || b.StudentId != null))
            .ToList();

        var userNames = await context.Users.AsNoTracking()
            .Select(u => new { u.Userid, u.Fullname })
            .ToDictionaryAsync(u => u.Userid, u => u.Fullname ?? u.Userid, ct);

        // Giữ Studentid để tra tiếp sang users: học sinh tự đăng ký không có hồ sơ.
        var studentByBooking = await context.Bookings.AsNoTracking()
            .Where(b => b.Studentid != null)
            .Select(b => new
            {
                b.Bookingid,
                b.Studentid,
                ProfileName = b.Student != null ? b.Student.Fullname : null,
            })
            .ToListAsync(ct);

        var settledCount = sessions
            .Where(s => s.Settled)
            .GroupBy(s => s.BookingId)
            .ToDictionary(g => g.Key, g => g.Count());

        var studentNames = await context.Studentprofiles.AsNoTracking()
            .Select(s => new { s.Studentid, s.Fullname })
            .ToDictionaryAsync(s => s.Studentid, s => s.Fullname ?? s.Studentid, ct);

        // Tên người học: ưu tiên hồ sơ, thiếu thì lấy từ tài khoản.
        var studentNameByBooking = studentByBooking.ToDictionary(
            x => x.Bookingid,
            x => x.ProfileName
                 ?? (userNames.TryGetValue(x.Studentid!, out var un) ? un : "—"));

        // Tra users trước, student_profiles sau: học sinh tự đăng ký không có hồ sơ
        // (hồ sơ chỉ sinh khi phụ huynh tạo cho con), tra ngược sẽ ra UUID.
        string CustomerName(BookingFlat b)
        {
            var id = CustomerKey(b);
            if (userNames.TryGetValue(id, out var un)) return un;
            if (studentNames.TryGetValue(id, out var sn)) return sn;
            return id;
        }

        var parents = revenueBookings
            .GroupBy(CustomerKey)
            .Select(g => new ParentRevenueDto
            {
                ParentId = g.Key,
                ParentName = CustomerName(g.First()),
                // Học sinh tự đặt thì người học chính là khách hàng.
                CustomerType = IsSelfBooking(g.First()) ? "Học sinh" : "Phụ huynh",
                StudentName = studentNameByBooking.TryGetValue(g.First().BookingId, out var sn2) ? sn2 : "—",
                TotalSpent = g.Sum(b => b.FinalPrice),
                BookingCount = g.Count(),
                SessionsPurchased = g.Sum(b => b.TotalSessions),
                SessionsCompleted = g.Sum(b => settledCount.TryGetValue(b.BookingId, out var c) ? c : 0),
                // Hoa hồng của buổi đã mua chưa học — cùng cơ sở với ComputeDeferred.
                DeferredRevenue = g.Sum(b =>
                    FeePerSession(b) * Math.Max(
                        b.TotalSessions - (settledCount.TryGetValue(b.BookingId, out var c2) ? c2 : 0),
                        0)),
                FirstBookingAt = g.Min(b => b.CreatedAt),
                LastBookingAt = g.Max(b => b.CreatedAt),
            })
            .OrderByDescending(p => p.TotalSpent)
            .Take(Math.Max(top, 1))
            .ToList();

        var allParents = revenueBookings.GroupBy(CustomerKey).ToList();
        var repeatNow = allParents.Count(g => g.Count() >= 2);
        var repeatRate = allParents.Count == 0 ? 0 : Math.Round((decimal)repeatNow / allParents.Count * 100, 1);

        var prevParents = revenueBookings
            .Where(b => b.CreatedAt < prevTo)
            .GroupBy(CustomerKey)
            .ToList();
        var repeatPrev = prevParents.Count == 0
            ? 0
            : Math.Round((decimal)prevParents.Count(g => g.Count() >= 2) / prevParents.Count * 100, 1);

        var inPeriod = revenueBookings.Where(b => b.CreatedAt >= fromUtc && b.CreatedAt < toUtc).ToList();
        var prevInPeriod = revenueBookings.Where(b => b.CreatedAt >= prevFrom && b.CreatedAt < prevTo).ToList();

        var activeParents = inPeriod.Select(CustomerKey).Distinct().Count();

        // ARPU theo từng tháng trong khoảng đang xem
        var arpu = new List<ArpuPointDto>();
        foreach (var ms in MonthBuckets(fromUtc, toUtc))
        {
            var me = ms.AddMonths(1);
            var monthRevenue = RecognisedIn(sessions, bookingById, ms, me);
            var monthParents = revenueBookings
                .Where(b => b.CreatedAt >= ms && b.CreatedAt < me)
                .Select(CustomerKey)
                .Distinct()
                .Count();
            arpu.Add(new ArpuPointDto
            {
                Month = MonthKey(ms),
                Arpu = monthParents == 0 ? 0 : Math.Round(monthRevenue / monthParents, 0),
                ActiveParents = monthParents,
            });
        }

        // Đếm theo KHÁCH (distinct), không theo booking — đặt 11 lần vẫn là 1 khách mới.
        var firstBookingByParent = revenueBookings
            .GroupBy(CustomerKey)
            .ToDictionary(g => g.Key, g => g.Min(b => b.CreatedAt));

        var newVsReturning = new List<NewVsReturningDto>();
        foreach (var ms in MonthBuckets(fromUtc, toUtc))
        {
            var me = ms.AddMonths(1);
            var monthCustomers = revenueBookings
                .Where(b => b.CreatedAt >= ms && b.CreatedAt < me)
                .Select(CustomerKey)
                .Distinct()
                .ToList();
            newVsReturning.Add(new NewVsReturningDto
            {
                Month = MonthKey(ms),
                NewCustomers = monthCustomers.Count(k =>
                    firstBookingByParent.TryGetValue(k, out var f) && f >= ms && f < me),
                Returning = monthCustomers.Count(k =>
                    firstBookingByParent.TryGetValue(k, out var f) && f < ms),
            });
        }

        var distribution = new[]
        {
            ("< 1 triệu", 0m, 1_000_000m),
            ("1 - 3 triệu", 1_000_000m, 3_000_000m),
            ("3 - 5 triệu", 3_000_000m, 5_000_000m),
            ("5 - 10 triệu", 5_000_000m, 10_000_000m),
            ("> 10 triệu", 10_000_000m, decimal.MaxValue),
        }.Select(r => new NamedCountDto
        {
            Range = r.Item1,
            Count = inPeriod.Count(b => b.FinalPrice >= r.Item2 && b.FinalPrice < r.Item3),
        }).ToList();

        // Cohort giữ chân — mỗi tháng trong khoảng là một hàng, bề rộng lưới bằng số tháng
        var cohortMonths = MonthBuckets(fromUtc, toUtc);
        var lastCohortIndex = cohortMonths.Count - 1;
        var cohorts = new List<CohortRowDto>();
        for (var c = 0; c <= lastCohortIndex; c++)
        {
            // Số tháng còn quan sát được sau cohort này — cohort càng cũ càng dài
            var i = lastCohortIndex - c;
            var ms = cohortMonths[c];
            var me = ms.AddMonths(1);
            var members = firstBookingByParent
                .Where(kv => kv.Value >= ms && kv.Value < me)
                .Select(kv => kv.Key)
                .ToHashSet();
            if (members.Count == 0)
            {
                cohorts.Add(new CohortRowDto { Cohort = MonthKey(ms), Size = 0, Retention = [] });
                continue;
            }

            var retention = new List<decimal?>();
            for (var m = 0; m <= i; m++)
            {
                var ws = ms.AddMonths(m);
                var we = ws.AddMonths(1);
                var active = revenueBookings
                    .Where(b => b.CreatedAt >= ws && b.CreatedAt < we && members.Contains(CustomerKey(b)))
                    .Select(CustomerKey)
                    .Distinct()
                    .Count();
                retention.Add(Math.Round((decimal)active / members.Count * 100, 0));
            }
            // Các tháng chưa tới: null để FE vẽ ô trống
            for (var m = i + 1; m <= lastCohortIndex; m++) retention.Add(null);

            cohorts.Add(new CohortRowDto
            {
                Cohort = MonthKey(ms),
                Size = members.Count,
                Retention = retention,
            });
        }

        var avgBookingValue = inPeriod.Count == 0 ? 0 : Math.Round(inPeriod.Average(b => b.FinalPrice), 0);
        var avgPrev = prevInPeriod.Count == 0 ? 0 : Math.Round(prevInPeriod.Average(b => b.FinalPrice), 0);

        // Doanh thu tính trong kỳ; Ltv và RepeatRate trên toàn lịch sử nhóm — cùng quy
        // ước với summary chung.
        var segments = new[] { ("Phụ huynh", false), ("Học sinh", true) }
            .Select(seg =>
            {
                var segInPeriod = inPeriod.Where(b => IsSelfBooking(b) == seg.Item2).ToList();
                var segAll = revenueBookings
                    .Where(b => IsSelfBooking(b) == seg.Item2)
                    .GroupBy(CustomerKey)
                    .ToList();

                return new CustomerSegmentDto
                {
                    Segment = seg.Item1,
                    Customers = segInPeriod.Select(CustomerKey).Distinct().Count(),
                    Bookings = segInPeriod.Count,
                    TotalSpent = segInPeriod.Sum(b => b.FinalPrice),
                    PlatformRevenue = segInPeriod.Sum(b =>
                        FeePerSession(b) * (settledCount.TryGetValue(b.BookingId, out var c) ? c : 0)),
                    Ltv = segAll.Count == 0
                        ? 0
                        : Math.Round(segAll.Sum(g => g.Sum(b => b.FinalPrice)) / segAll.Count, 0),
                    AvgBookingValue = segInPeriod.Count == 0
                        ? 0
                        : Math.Round(segInPeriod.Average(b => b.FinalPrice), 0),
                    RepeatRate = segAll.Count == 0
                        ? 0
                        : Math.Round((decimal)segAll.Count(g => g.Count() >= 2) / segAll.Count * 100, 1),
                };
            })
            .ToList();

        return new AdminCustomerRevenueResponse
        {
            Summary = new CustomerSummaryDto
            {
                ActiveParents = activeParents,
                RepeatRate = repeatRate,
                RepeatRatePrevious = repeatPrev,
                AvgBookingValue = avgBookingValue,
                AvgBookingValuePrevious = avgPrev,
                Ltv = allParents.Count == 0
                    ? 0
                    : Math.Round(revenueBookings.Sum(b => b.FinalPrice) / allParents.Count, 0),
            },
            Segments = segments,
            Parents = parents,
            ArpuTrend = arpu,
            NewVsReturning = newVsReturning,
            BookingValueDistribution = distribution,
            Cohorts = cohorts,
        };
    }
}
