using Microsoft.EntityFrameworkCore;
using MV.DomainLayer.DTO.ResponseModel.Admin;

namespace MV.ApplicationLayer.Services;

public partial class AdminRevenueAnalyticsService
{
    public async Task<AdminSubjectRevenueResponse> GetSubjectRevenueAsync(
        DateTime? from, DateTime? to, CancellationToken ct = default)
    {
        var (fromUtc, toUtc) = Normalise(from, to);

        var bookings = await LoadBookingsAsync(ct);
        var sessions = await LoadSessionsAsync(ct);

        var subjectNames = await context.Subjects.AsNoTracking()
            .ToDictionaryAsync(s => s.Subjectid, s => s.Subjectname, ct);
        var gradeNames = await context.Gradelevels.AsNoTracking()
            .ToDictionaryAsync(g => g.Gradelevelid, g => g.Gradename, ct);

        var revenueBookings = bookings
            .Where(b => RevenueBookingStatuses.Contains(b.Status ?? "")
                        && b.CreatedAt >= fromUtc && b.CreatedAt < toUtc)
            .ToList();

        var settledByBooking = sessions
            .Where(s => s.Settled && s.When >= fromUtc && s.When < toUtc)
            .GroupBy(s => s.BookingId)
            .ToDictionary(g => g.Key, g => g.Count());

        var subjects = revenueBookings
            .Where(b => b.SubjectId.HasValue)
            .GroupBy(b => b.SubjectId!.Value)
            .Select(g =>
            {
                var deliveredSessions = g.Sum(b => settledByBooking.TryGetValue(b.BookingId, out var c) ? c : 0);
                var purchased = g.Sum(b => b.TotalSessions);
                return new SubjectRevenueDto
                {
                    SubjectId = g.Key,
                    SubjectName = subjectNames.TryGetValue(g.Key, out var n) ? n : $"#{g.Key}",
                    Gmv = g.Sum(b => b.FinalPrice),
                    PlatformRevenue = g.Sum(b =>
                        FeePerSession(b) * (settledByBooking.TryGetValue(b.BookingId, out var c) ? c : 0)),
                    // Hoa hồng của buổi đã bán mà chưa dạy — cùng cơ sở với ComputeDeferred.
                    DeferredRevenue = g.Sum(b =>
                        FeePerSession(b) * Math.Max(
                            b.TotalSessions - (settledByBooking.TryGetValue(b.BookingId, out var c) ? c : 0),
                            0)),
                    Bookings = g.Count(),
                    SessionsDelivered = deliveredSessions,
                    AvgPricePerSession = purchased == 0
                        ? 0
                        : Math.Round(g.Sum(b => b.FinalPrice) / purchased, 0),
                    CompletionRate = purchased == 0
                        ? 0
                        : Math.Round((decimal)deliveredSessions / purchased * 100, 1),
                };
            })
            .OrderByDescending(s => s.PlatformRevenue)
            .ToList();

        var grades = revenueBookings
            .Where(b => b.GradeId.HasValue)
            .GroupBy(b => b.GradeId!.Value)
            .Select(g => new GradeRevenueDto
            {
                GradeId = g.Key,
                GradeName = gradeNames.TryGetValue(g.Key, out var n) ? n : $"Lớp {g.Key}",
                Gmv = g.Sum(b => b.FinalPrice),
                PlatformRevenue = g.Sum(b =>
                    FeePerSession(b) * (settledByBooking.TryGetValue(b.BookingId, out var c) ? c : 0)),
                Bookings = g.Count(),
            })
            .OrderByDescending(g => g.PlatformRevenue)
            .ToList();

        var matrix = revenueBookings
            .Where(b => b.SubjectId.HasValue && b.GradeId.HasValue)
            .GroupBy(b => new { S = b.SubjectId!.Value, G = b.GradeId!.Value })
            .Select(g => new SubjectGradeCellDto
            {
                Subject = subjectNames.TryGetValue(g.Key.S, out var sn) ? sn : $"#{g.Key.S}",
                Grade = gradeNames.TryGetValue(g.Key.G, out var gn) ? gn : $"Lớp {g.Key.G}",
                Revenue = g.Sum(b =>
                    FeePerSession(b) * (settledByBooking.TryGetValue(b.BookingId, out var c) ? c : 0)),
            })
            .ToList();

        // Xu hướng theo khoảng đang xem, cho tối đa 6 môn hàng đầu.
        // Chỉ tính buổi thuộc revenueBookings — cùng tập với bảng và heatmap, nếu không
        // tổng các cột sẽ vượt platformRevenue.
        var revenueBookingById = revenueBookings.ToDictionary(b => b.BookingId);
        var topSubjects = subjects.Take(6).ToList();
        var trend = new List<Dictionary<string, object>>();
        foreach (var ms in MonthBuckets(fromUtc, toUtc))
        {
            var me = ms.AddMonths(1);
            var point = new Dictionary<string, object> { ["month"] = MonthKey(ms) };
            var monthSettled = sessions
                .Where(s => s.Settled && s.When >= ms && s.When < me
                            && revenueBookingById.ContainsKey(s.BookingId))
                .ToList();
            foreach (var subj in topSubjects)
            {
                point[subj.SubjectName] = monthSettled
                    .Where(s => revenueBookingById[s.BookingId].SubjectId == subj.SubjectId)
                    .Sum(s => FeePerSession(revenueBookingById[s.BookingId]));
            }
            trend.Add(point);
        }

        return new AdminSubjectRevenueResponse
        {
            Subjects = subjects,
            Grades = grades,
            Matrix = matrix,
            SubjectTrend = trend,
        };
    }
}
