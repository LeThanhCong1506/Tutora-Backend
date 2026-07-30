using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.ResponseModel.Admin;

namespace MV.ApplicationLayer.Services;

public partial class AdminRevenueAnalyticsService
{
    public async Task<AdminTutorRevenueResponse> GetTutorRevenueAsync(
        DateTime? from, DateTime? to, int top, CancellationToken ct = default)
    {
        var (fromUtc, toUtc) = Normalise(from, to);

        var bookings = await LoadBookingsAsync(ct);
        var sessions = await LoadSessionsAsync(ct);
        var bookingById = bookings.ToDictionary(b => b.BookingId);

        var profiles = await context.Tutorprofiles.AsNoTracking()
            .Select(t => new
            {
                t.Tutorid,
                Name = t.Tutor != null ? t.Tutor.Fullname : null,
                t.Averagerating,
            })
            .ToListAsync(ct);
        var profileById = profiles
            .Where(p => p.Tutorid != null)
            .ToDictionary(p => p.Tutorid!);

        // Gia sư xoá mềm không có hồ sơ (query filter) nhưng buổi dạy vẫn còn → fallback.
        var userFullNames = await context.Users.AsNoTracking()
            .Select(u => new { u.Userid, u.Fullname })
            .ToDictionaryAsync(u => u.Userid, u => u.Fullname, ct);

        var escrow = await context.Wallets.AsNoTracking()
            .Where(w => w.Userid != null)
            .Select(w => new { w.Userid, Frozen = w.Frozenbalance ?? 0 })
            .ToDictionaryAsync(w => w.Userid!, w => w.Frozen, ct);

        var disputes = await context.Disputes.AsNoTracking()
            .Where(d => d.Classsessionid != null)
            .Select(d => d.Classsessionid)
            .ToListAsync(ct);
        var disputeSessionIds = disputes.Where(d => d.HasValue).Select(d => d!.Value).ToHashSet();

        var sessionTutorDispute = await context.ClassSessions.AsNoTracking()
            .Where(l => disputeSessionIds.Contains(l.Classsessionid))
            .GroupBy(l => l.Tutorid)
            .Select(g => new { TutorId = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var disputeByTutor = sessionTutorDispute
            .Where(x => x.TutorId != null)
            .ToDictionary(x => x.TutorId!, x => x.Count);

        var subjectNames = await context.Subjects.AsNoTracking()
            .ToDictionaryAsync(s => s.Subjectid, s => s.Subjectname, ct);

        // Buổi trong kỳ, gom theo gia sư
        var periodSessions = sessions
            .Where(s => s.When >= fromUtc && s.When < toUtc && s.TutorId != null)
            .ToList();

        var rows = new List<TutorRevenueDto>();
        foreach (var g in periodSessions.GroupBy(s => s.TutorId!))
        {
            var delivered = g.Count(s => s.Settled);
            var cancelled = g.Count(s =>
                s.Status is ClassSessionStatus.Cancelled
                    or ClassSessionStatus.CancelledNoshow
                    or ClassSessionStatus.NoShow);
            var denom = delivered + cancelled;

            var revenue = g.Where(s => s.Settled)
                .Sum(s => bookingById.TryGetValue(s.BookingId, out var b) ? FeePerSession(b) : 0);

            var tutorBookings = bookings
                .Where(b => b.TutorId == g.Key
                            && RevenueBookingStatuses.Contains(b.Status ?? "")
                            && b.CreatedAt >= fromUtc && b.CreatedAt < toUtc)
                .ToList();

            var profile = profileById.TryGetValue(g.Key, out var pf) ? pf : null;
            var mainSubject = tutorBookings
                .Where(b => b.SubjectId.HasValue)
                .GroupBy(b => b.SubjectId!.Value)
                .OrderByDescending(x => x.Count())
                .Select(x => subjectNames.TryGetValue(x.Key, out var n) ? n : "—")
                .FirstOrDefault() ?? "—";

            var gmv = tutorBookings.Sum(b => b.FinalPrice);

            rows.Add(new TutorRevenueDto
            {
                TutorId = g.Key,
                // Hồ sơ → tài khoản → UUID. Gia sư xoá mềm rơi vào nhánh giữa.
                TutorName = profile?.Name
                            ?? (userFullNames.TryGetValue(g.Key, out var un) ? un : null)
                            ?? g.Key,
                Subject = mainSubject,
                Gmv = gmv,
                PlatformRevenue = revenue,
                // Chỉ so sánh tương đối: GMV theo booking tạo trong kỳ, doanh thu theo
                // buổi đã dạy — hai mốc khác nhau.
                TakeRate = gmv == 0 ? 0 : Math.Round(revenue / gmv * 100, 1),
                TutorEarnings = g.Where(s => s.Settled)
                    .Sum(s => bookingById.TryGetValue(s.BookingId, out var b) && b.TotalSessions > 0
                        ? Math.Round(b.TutorFee / b.TotalSessions, 2)
                        : 0),
                EscrowHeld = escrow.TryGetValue(g.Key, out var fz) ? fz : 0,
                SessionsDelivered = delivered,
                RevenuePerSession = delivered == 0 ? 0 : Math.Round(revenue / delivered, 0),
                CancelRate = denom == 0 ? 0 : Math.Round((decimal)cancelled / denom * 100, 1),
                DisputeCount = disputeByTutor.TryGetValue(g.Key, out var dc) ? dc : 0,
                Rating = profile?.Averagerating is { } r ? Math.Round((decimal)r, 2) : 0,
            });
        }

        rows = rows.OrderByDescending(r => r.PlatformRevenue).ToList();

        var totalRevenue = rows.Sum(r => r.PlatformRevenue);
        var top10 = rows.Take(10).Sum(r => r.PlatformRevenue);
        var next40 = rows.Skip(10).Take(40).Sum(r => r.PlatformRevenue);
        var rest = totalRevenue - top10 - next40;

        // Escrow là số dư hiện tại, không thuộc kỳ nào — cộng toàn bộ ví gia sư để con
        // số không nhảy khi đổi preset.
        var tutorIds = profiles.Where(p => p.Tutorid != null).Select(p => p.Tutorid!).ToHashSet();
        var totalEscrow = escrow.Where(kv => tutorIds.Contains(kv.Key)).Sum(kv => kv.Value);

        // Nghiệp vụ không cho xoá gia sư khi ví còn tiền — rơi vào đây là dữ liệu bị sửa
        // tay. Chỉ soi role Tutor vì ví phụ huynh cũng có frozen hợp lệ.
        var tutorRoleIds = await context.Users.AsNoTracking()
            .Where(u => u.Primaryrole == UserRole.Tutor)
            .Select(u => u.Userid)
            .ToListAsync(ct);
        var orphanEscrow = escrow
            .Where(kv => tutorRoleIds.Contains(kv.Key) && !tutorIds.Contains(kv.Key) && kv.Value > 0)
            .ToList();
        if (orphanEscrow.Count > 0)
        {
            logger.LogWarning(
                "Escrow mồ côi: {Count} ví còn {Amount} nhưng không có hồ sơ gia sư hiện hữu "
                + "(gia sư bị xoá mềm khi ví chưa giải ngân?). UserIds={Ids}",
                orphanEscrow.Count,
                orphanEscrow.Sum(kv => kv.Value),
                string.Join(",", orphanEscrow.Select(kv => kv.Key).Take(20)));
        }

        return new AdminTutorRevenueResponse
        {
            Tutors = rows.Take(Math.Max(top, 1)).ToList(),
            // Đếm trên toàn bộ rows, không phải danh sách đã cắt Take(top).
            TutorsWithRevenue = rows.Count(r => r.SessionsDelivered > 0),
            ActiveTutors = rows.Count,
            Concentration =
            [
                new() { Name = "Top 10 gia sư", Value = top10 },
                new() { Name = "Gia sư 11-50", Value = next40 },
                new() { Name = "Còn lại", Value = Math.Max(rest, 0) },
            ],
            TotalPlatformRevenue = totalRevenue,
            TotalEscrowHeld = totalEscrow,
        };
    }
}
