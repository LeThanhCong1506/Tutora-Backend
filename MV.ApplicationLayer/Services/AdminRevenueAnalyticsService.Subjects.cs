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
        var ledger = await LoadBookingLedgerAsync(ct);

        var subjectNames = await context.Subjects.AsNoTracking()
            .ToDictionaryAsync(s => s.Subjectid, s => s.Subjectname, ct);
        var gradeNames = await context.Gradelevels.AsNoTracking()
            .ToDictionaryAsync(g => g.Gradelevelid, g => g.Gradename, ct);

        var bookingById = bookings.ToDictionary(b => b.BookingId);
        var closed = BuildClosedBookings(bookings, sessions, ledger);
        var keptByBooking = closed.ToDictionary(c => c.BookingId, c => c.PlatformKept);

        /// Tập COHORT của kỳ — dùng cho các cột đếm theo lần ĐẶT lịch: Gmv, số booking,
        /// giá trung bình, tỷ lệ hoàn thành, nợ dịch vụ.
        var revenueBookings = CohortBookings(bookings, closed)
            .Where(b => b.CreatedAt >= fromUtc && b.CreatedAt < toUtc)
            .ToList();

        var settledByBooking = sessions
            .Where(s => s.Settled && s.When >= fromUtc && s.When < toUtc)
            .GroupBy(s => s.BookingId)
            .ToDictionary(g => g.Key, g => g.Count());

        // Luỹ kế tới cuối kỳ — dùng cho tiền (đã chín / còn chờ). Khác `settledByBooking` ở
        // trên, vốn chỉ đếm buổi TRONG kỳ và chỉ dùng cho cột tiến độ.
        var deliveries = BuildDeliveries(sessions, toUtc);

        // ─── Doanh thu của tab này neo theo NGÀY DẠY, không theo ngày đặt ────────────
        //
        // Bản cũ tính `keptByBooking[b] ?? đơn giá × buổi settle trong kỳ` trên cohort của kỳ.
        // Hai lỗi:
        //
        //   1. Với khoá đã đóng sổ nó lấy `kept` — số THỰC GIỮ CẢ ĐỜI khoá — mà không hỏi khoá
        //      đó đóng lúc nào. Một khoá đặt tháng 8, đóng tháng 10 sẽ ghi trọn doanh thu vào
        //      báo cáo tháng 8, tức báo cáo quá khứ tự đổi số khi mở lại sau này.
        //   2. Nó chỉ xét khoá TẠO trong kỳ, nên buổi dạy tháng này của khoá đặt tháng trước
        //      rơi hết ra ngoài — trong khi tab Gia sư lại đếm đúng những buổi đó.
        //
        // Kết quả: tổng doanh thu tab Môn ≠ tổng tab Gia sư, dù hai bên chỉ là hai cách cắt
        // của CÙNG một khoản tiền, và cả hai đều mang nhãn "doanh thu đã ghi nhận".
        //
        // Nay dùng đúng quy tắc của tab Gia sư và của đường "Doanh thu đã ghi nhận" ở tab
        // Doanh thu: phí của buổi đã dạy trong kỳ, cộng phần chênh chốt tại ngày đóng sổ của
        // các khoá đóng trong kỳ. Ba tab giờ cộng ra cùng một con số.
        var sessionRevenueBySubject = sessions
            .Where(s => s.Settled && s.When >= fromUtc && s.When < toUtc
                        && bookingById.ContainsKey(s.BookingId)
                        && bookingById[s.BookingId].SubjectId.HasValue)
            .GroupBy(s => bookingById[s.BookingId].SubjectId!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(s => TutorCutPerSession(bookingById[s.BookingId])));

        // Vế thứ hai của doanh thu: phí phụ huynh, chín ở mốc muộn hơn giữa ngày trả tiền và
        // ngày buổi ĐẦU dạy xong. Cùng hàm với tab Doanh thu nên ba tab vẫn cộng ra một số.
        var parentFeeBySubject = bookings
            .Where(b => b.SubjectId.HasValue)
            .GroupBy(b => b.SubjectId!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(b =>
                ParentFeeRecognisedIn(b, fromUtc, toUtc, DeliveryOf(deliveries, b.BookingId))));

        var closingBySubject = closed
            .Where(c => c.When >= fromUtc && c.When < toUtc && c.SubjectId.HasValue)
            .GroupBy(c => c.SubjectId!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(c => c.Adjustment));

        decimal RecognisedBySubject(int subjectId) =>
            (sessionRevenueBySubject.TryGetValue(subjectId, out var s) ? s : 0)
            + (parentFeeBySubject.TryGetValue(subjectId, out var p) ? p : 0)
            + (closingBySubject.TryGetValue(subjectId, out var a) ? a : 0);

        // Cùng công thức, cắt theo KHỐI LỚP.
        var sessionRevenueByGrade = sessions
            .Where(s => s.Settled && s.When >= fromUtc && s.When < toUtc
                        && bookingById.ContainsKey(s.BookingId)
                        && bookingById[s.BookingId].GradeId.HasValue)
            .GroupBy(s => bookingById[s.BookingId].GradeId!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(s => TutorCutPerSession(bookingById[s.BookingId])));

        var parentFeeByGrade = bookings
            .Where(b => b.GradeId.HasValue)
            .GroupBy(b => b.GradeId!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(b =>
                ParentFeeRecognisedIn(b, fromUtc, toUtc, DeliveryOf(deliveries, b.BookingId))));

        var closingByGrade = closed
            .Where(c => c.When >= fromUtc && c.When < toUtc && c.GradeId.HasValue)
            .GroupBy(c => c.GradeId!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(c => c.Adjustment));

        decimal RecognisedByGrade(int gradeId) =>
            (sessionRevenueByGrade.TryGetValue(gradeId, out var s) ? s : 0)
            + (parentFeeByGrade.TryGetValue(gradeId, out var p) ? p : 0)
            + (closingByGrade.TryGetValue(gradeId, out var a) ? a : 0);

        // Nợ dịch vụ chỉ có ở booking đang chạy — buổi chưa dạy của khoá đã đóng sổ đã bị huỷ.
        decimal DeferredOf(BookingFlat b) =>
            keptByBooking.ContainsKey(b.BookingId)
                ? 0
                : UnearnedSoFar(b, toUtc, DeliveryOf(deliveries, b.BookingId));

        // Danh sách môn = HỢP của hai nguồn, không chỉ cohort.
        //
        // Một môn có buổi dạy trong kỳ nhưng không có lịch nào ĐẶT trong kỳ vẫn phải có dòng,
        // nếu không doanh thu của nó biến mất khỏi bảng và tổng chân bảng hụt so với tab Gia sư.
        // Ngược lại, môn có lịch đặt mới mà chưa dạy buổi nào cũng phải có dòng để cột "Khách
        // trả" và "Còn chờ" nhìn thấy được.
        var cohortBySubject = revenueBookings
            .Where(b => b.SubjectId.HasValue)
            .GroupBy(b => b.SubjectId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        var subjectIds = cohortBySubject.Keys
            .Union(sessionRevenueBySubject.Keys)
            .Union(closingBySubject.Keys)
            .ToList();

        var subjects = subjectIds
            .Select(id =>
            {
                var g = cohortBySubject.TryGetValue(id, out var list) ? list : [];
                var deliveredSessions = g.Sum(b => settledByBooking.TryGetValue(b.BookingId, out var c) ? c : 0);
                var purchased = g.Sum(b => b.TotalSessions);
                var gmv = g.Sum(b => b.FinalPrice);
                return new SubjectRevenueDto
                {
                    SubjectId = id,
                    SubjectName = subjectNames.TryGetValue(id, out var n) ? n : $"#{id}",
                    Gmv = gmv,
                    PlatformRevenue = RecognisedBySubject(id),
                    // Phí của buổi đã bán mà chưa dạy — cùng cơ sở với ComputeDeferred.
                    DeferredRevenue = g.Sum(DeferredOf),
                    Bookings = g.Count,
                    SessionsDelivered = deliveredSessions,
                    AvgPricePerSession = purchased == 0 ? 0 : Math.Round(gmv / purchased, 0),
                    CompletionRate = purchased == 0
                        ? 0
                        : Math.Round((decimal)deliveredSessions / purchased * 100, 1),
                };
            })
            .OrderByDescending(s => s.PlatformRevenue)
            .ToList();

        var cohortByGrade = revenueBookings
            .Where(b => b.GradeId.HasValue)
            .GroupBy(b => b.GradeId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        var grades = cohortByGrade.Keys
            .Union(sessionRevenueByGrade.Keys)
            .Union(closingByGrade.Keys)
            .Select(id =>
            {
                var g = cohortByGrade.TryGetValue(id, out var list) ? list : [];
                return new GradeRevenueDto
                {
                    GradeId = id,
                    GradeName = gradeNames.TryGetValue(id, out var n) ? n : $"Lớp {id}",
                    Gmv = g.Sum(b => b.FinalPrice),
                    PlatformRevenue = RecognisedByGrade(id),
                    Bookings = g.Count,
                };
            })
            .OrderByDescending(g => g.PlatformRevenue)
            .ToList();

        // Heatmap môn × khối — cùng quy tắc ghi nhận, cắt theo cặp (môn, khối) của booking
        // mà buổi học thuộc về. Cộng cả lưới ra đúng tổng của bảng.
        var matrixCells = new Dictionary<(int S, int G), decimal>();
        foreach (var s in sessions.Where(x => x.Settled && x.When >= fromUtc && x.When < toUtc))
        {
            if (!bookingById.TryGetValue(s.BookingId, out var b)) continue;
            if (!b.SubjectId.HasValue || !b.GradeId.HasValue) continue;
            var key = (b.SubjectId.Value, b.GradeId.Value);
            matrixCells[key] = (matrixCells.TryGetValue(key, out var v) ? v : 0) + TutorCutPerSession(b);
        }
        foreach (var b in bookings)
        {
            if (!b.SubjectId.HasValue || !b.GradeId.HasValue) continue;
            var parentFee = ParentFeeRecognisedIn(b, fromUtc, toUtc, DeliveryOf(deliveries, b.BookingId));
            if (parentFee == 0) continue;
            var key = (b.SubjectId.Value, b.GradeId.Value);
            matrixCells[key] = (matrixCells.TryGetValue(key, out var v) ? v : 0) + parentFee;
        }
        foreach (var c in closed.Where(x => x.When >= fromUtc && x.When < toUtc))
        {
            if (!c.SubjectId.HasValue || !c.GradeId.HasValue) continue;
            var key = (c.SubjectId.Value, c.GradeId.Value);
            matrixCells[key] = (matrixCells.TryGetValue(key, out var v) ? v : 0) + c.Adjustment;
        }

        var matrix = matrixCells
            .Select(kv => new SubjectGradeCellDto
            {
                Subject = subjectNames.TryGetValue(kv.Key.S, out var sn) ? sn : $"#{kv.Key.S}",
                Grade = gradeNames.TryGetValue(kv.Key.G, out var gn) ? gn : $"Lớp {kv.Key.G}",
                Revenue = kv.Value,
            })
            .ToList();

        // Xu hướng theo khoảng đang xem, cho tối đa 6 môn hàng đầu.
        //
        // Không còn lọc theo cohort như bản cũ: cột doanh thu của bảng giờ neo theo ngày dạy,
        // nên các mốc thời gian phải neo y hệt thì cộng các cột mới ra đúng tổng của bảng.
        var topSubjects = subjects.Take(6).ToList();
        var trend = new List<Dictionary<string, object>>();
        foreach (var (ms, me, label) in TimeBuckets(fromUtc, toUtc))
        {
            var point = new Dictionary<string, object> { ["month"] = label };
            // Buổi đã dạy của MỌI khoá, kể cả khoá về sau bị huỷ — phí của buổi đó thuộc đúng
            // tháng dạy. Phần chênh khi đóng sổ cộng riêng bên dưới.
            var monthSettled = sessions
                .Where(s => s.Settled && s.When >= ms && s.When < me
                            && bookingById.ContainsKey(s.BookingId))
                .ToList();
            var monthClosed = closed.Where(c => c.When >= ms && c.When < me).ToList();
            foreach (var subj in topSubjects)
            {
                point[subj.SubjectName] = monthSettled
                    .Where(s => bookingById[s.BookingId].SubjectId == subj.SubjectId)
                    .Sum(s => TutorCutPerSession(bookingById[s.BookingId]))
                    + bookings
                        .Where(b => b.SubjectId == subj.SubjectId)
                        .Sum(b => ParentFeeRecognisedIn(b, ms, me, DeliveryOf(deliveries, b.BookingId)))
                    + monthClosed
                        .Where(c => c.SubjectId == subj.SubjectId)
                        .Sum(c => c.Adjustment);
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
