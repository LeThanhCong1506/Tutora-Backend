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
        var ledger = await LoadBookingLedgerAsync(ct);
        var bookingById = bookings.ToDictionary(b => b.BookingId);
        var closed = BuildClosedBookings(bookings, sessions, ledger);
        var cohort = CohortBookings(bookings, closed);

        // Phần chênh chốt tại ngày đóng sổ, gom theo gia sư. Hoa hồng và thu nhập của từng buổi
        // đã dạy được đếm thẳng từ `sessions` bên dưới (kể cả buổi thuộc khoá về sau bị huỷ),
        // nên ở đây CHỈ cộng phần chênh — cộng cả PlatformKept là tính hai lần.
        var closedInPeriod = closed
            .Where(c => c.When >= fromUtc && c.When < toUtc && c.TutorId != null)
            .GroupBy(c => c.TutorId!)
            .ToDictionary(g => g.Key, g => new
            {
                TutorCutAdjustment = g.Sum(c => c.TutorCutAdjustment),
                TutorAdjustment = g.Sum(c => c.TutorAdjustment),
            });

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

        // Lọc theo kỳ như mọi cột khác của bảng. Trước đây đếm khiếu nại của MỌI thời điểm,
        // nên cột "Khiếu nại" là con số duy nhất trong bảng không đổi khi người dùng đổi mốc
        // thời gian — một gia sư bị khiếu nại từ nửa năm trước vẫn hiện số đỏ ở báo cáo tuần này.
        var disputes = await context.Disputes.AsNoTracking()
            .Where(d => d.Classsessionid != null
                        && d.Createdat >= fromUtc && d.Createdat < toUtc)
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
        var sessionsByTutor = sessions
            .Where(s => s.When >= fromUtc && s.When < toUtc && s.TutorId != null)
            .GroupBy(s => s.TutorId!)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Gia sư có mặt trong kỳ = có buổi trong kỳ, HOẶC có khoá đóng sổ trong kỳ. Thiếu vế
        // thứ hai thì gia sư dạy từ kỳ trước mà khoá bị huỷ ở kỳ này sẽ không có dòng nào, dù
        // tiền của buổi đã dạy vừa được giải ngân cho họ trong chính kỳ này.
        //
        // Từ 01/09/2026 tab này CHỈ báo vế phí gia sư (xem `revenue` bên dưới), mà vế đó chỉ
        // sinh ra từ buổi đã dạy hoặc từ lúc chốt sổ — đúng hai vế trên, không cần gom thêm.
        var activeTutorIds = sessionsByTutor.Keys.Union(closedInPeriod.Keys).ToList();

        var rows = new List<TutorRevenueDto>();
        foreach (var tutorId in activeTutorIds)
        {
            var g = sessionsByTutor.TryGetValue(tutorId, out var ts) ? ts : [];
            var fromClosed = closedInPeriod.TryGetValue(tutorId, out var cl) ? cl : null;

            // Buổi đã dạy của MỌI khoá đều tính, kể cả khoá về sau bị huỷ: công đã làm là công
            // đã làm. Cùng bộ lọc với doanh thu bên dưới để "hoa hồng mỗi buổi" không bị chia
            // sai mẫu số.
            var delivered = g.Count(s => s.Settled && IsRevenueSession(s, bookingById));
            var cancelled = g.Count(s =>
                s.Status is ClassSessionStatus.Cancelled
                    or ClassSessionStatus.CancelledNoshow
                    or ClassSessionStatus.NoShow);
            var denom = delivered + cancelled;

            // ─── Tab này chỉ báo vế PHÍ GIA SƯ, không phải trọn 10% ─────────────────
            //
            // Phí sàn 10% đến từ HAI nguồn khác nhau: 5% cắt từ tiền gia sư, và 5% phụ huynh
            // trả thêm. Gộp cả hai rồi treo dưới tên một gia sư là nói rằng người đó mang về
            // cho sàn cả khoản mà PHỤ HUYNH trả — trong khi phần ấy thuộc về câu chuyện khách
            // hàng, và đã được báo ở tab Khách hàng.
            //
            // Nên ở đây chỉ lấy 5% cắt từ gia sư, của những buổi họ ĐÃ DẠY trong kỳ, cộng lát
            // phí gia sư trong phần chênh khi chốt sổ. Hệ quả có chủ ý: tổng tab này KHÔNG còn
            // bằng "Doanh thu đã ghi nhận" ở tab Doanh thu — nó là một nửa của con số đó, nửa
            // kia nằm ở tab Khách hàng.
            var revenue = g.Where(s => s.Settled && IsRevenueSession(s, bookingById))
                .Sum(s => TutorCutPerSession(bookingById[s.BookingId]))
                + (fromClosed?.TutorCutAdjustment ?? 0);

            var tutorBookings = cohort
                .Where(b => b.TutorId == tutorId
                            && b.CreatedAt >= fromUtc && b.CreatedAt < toUtc)
                .ToList();

            var profile = profileById.TryGetValue(tutorId, out var pf) ? pf : null;
            var mainSubject = tutorBookings
                .Where(b => b.SubjectId.HasValue)
                .GroupBy(b => b.SubjectId!.Value)
                .OrderByDescending(x => x.Count())
                .Select(x => subjectNames.TryGetValue(x.Key, out var n) ? n : "—")
                .FirstOrDefault() ?? "—";

            var gmv = tutorBookings.Sum(b => b.FinalPrice);

            rows.Add(new TutorRevenueDto
            {
                TutorId = tutorId,
                // Hồ sơ → tài khoản → UUID. Gia sư xoá mềm rơi vào nhánh giữa.
                TutorName = profile?.Name
                            ?? (userFullNames.TryGetValue(tutorId, out var un) ? un : null)
                            ?? tutorId,
                Subject = mainSubject,
                Gmv = gmv,
                TutorFeeRevenue = revenue,
                // Chỉ so sánh tương đối: GMV theo booking tạo trong kỳ, doanh thu theo
                // buổi đã dạy — hai mốc khác nhau. Không còn màn hình nào đọc (cột "Tỷ lệ giữ
                // lại" đã bỏ 01/09/2026 vì lý do đó).
                TakeRate = gmv == 0 ? 0 : Math.Round(revenue / gmv * 100, 1),
                // Đơn giá × buổi đã dạy, cộng phần chênh so với số THỰC giải ngân trong ví khi
                // khoá đóng sổ. Phần chênh thường bằng 0; nó khác 0 ở ca hoàn tiền một phần theo
                // khiếu nại, nơi gia sư chỉ nhận một phần buổi đó.
                TutorEarnings = g.Where(s => s.Settled && IsRevenueSession(s, bookingById))
                    .Sum(s => bookingById[s.BookingId].TotalSessions > 0
                        ? Math.Round(bookingById[s.BookingId].TutorFee
                                     / bookingById[s.BookingId].TotalSessions, 2)
                        : 0)
                    + (fromClosed?.TutorAdjustment ?? 0),
                EscrowHeld = escrow.TryGetValue(tutorId, out var fz) ? fz : 0,
                SessionsDelivered = delivered,
                RevenuePerSession = delivered == 0 ? 0 : Math.Round(revenue / delivered, 0),
                CancelRate = denom == 0 ? 0 : Math.Round((decimal)cancelled / denom * 100, 1),
                DisputeCount = disputeByTutor.TryGetValue(tutorId, out var dc) ? dc : 0,
                Rating = profile?.Averagerating is { } r ? Math.Round((decimal)r, 2) : 0,
            });
        }

        rows = rows.OrderByDescending(r => r.TutorFeeRevenue).ToList();

        var totalRevenue = rows.Sum(r => r.TutorFeeRevenue);
        var top10 = rows.Take(10).Sum(r => r.TutorFeeRevenue);
        var next40 = rows.Skip(10).Take(40).Sum(r => r.TutorFeeRevenue);
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
            TotalTutorFeeRevenue = totalRevenue,
            TotalEscrowHeld = totalEscrow,
        };
    }
}
