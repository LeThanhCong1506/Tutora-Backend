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
        var ledger = await LoadBookingLedgerAsync(ct);
        var bookingById = bookings.ToDictionary(b => b.BookingId);
        var closed = BuildClosedBookings(bookings, sessions, ledger);
        var keptByBooking = closed.ToDictionary(c => c.BookingId, c => c.PlatformKept);
        // Hoàn tiền theo từng booking — cột "Đã hoàn" của bảng chi tiết. Lấy từ cùng một sổ ví
        // với phần tính doanh thu, nên hai con số trên cùng một dòng luôn khớp nhau.
        var refundedByBooking = ledger.ToDictionary(kv => kv.Key, kv => kv.Value.Refunded);
        // Nợ dịch vụ và tuổi nợ chỉ tính trên booking CHƯA CHỐT SỔ: buổi chưa dạy của booking đã
        // đóng sổ đã bị huỷ và hoàn tiền, không còn là nghĩa vụ phải giao.
        // Cùng tập với BuildClosedBookings — hai bên phải nhất trí booking nào đã chốt sổ.
        var nothingLeft = NothingLeftToTeach(sessions);
        var openBookings = bookings.Where(x => IsOpen(x, nothingLeft)).ToList();
        var cohortBookings = CohortBookings(bookings, closed);

        var overview = await GetOverviewAsync(from, to, ct);

        var deliveries = BuildDeliveries(sessions);
        var settledCount = deliveries.ToDictionary(kv => kv.Key, kv => kv.Value.Count);

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

        foreach (var b in openBookings)
        {
            if (b.CreatedAt == null) continue;
            var unearned = UnearnedSoFar(b, now, DeliveryOf(deliveries, b.BookingId));
            if (unearned == 0) continue;

            var age = (int)(now - b.CreatedAt.Value).TotalDays;
            var idx = Array.FindIndex(buckets, bk => age >= bk.MinDays && age <= bk.MaxDays);
            if (idx < 0) idx = buckets.Length - 1;

            aging[idx].Amount += unearned;
            aging[idx].Bookings += 1;
        }

        // Booking chết sau đợt 1: đã trả deposit, quá hạn trả đợt 2, chưa hoàn tất.
        // Xét toàn bộ booking: ca huỷ mang status cancelled, nằm ngoài openBookings.
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
            // Phần chưa chín mới là phần còn có thể mất. Khoá đã qua buổi đầu thì phí phụ huynh
            // đã nằm chắc trong két — khách bỏ dở cũng không lấy lại được, nên không tính vào đây.
            ContractedFeeAtRisk = stalledNow.Sum(b =>
                UnearnedSoFar(b, now, DeliveryOf(deliveries, b.BookingId))),
            DropOffRate = depositCohortNow == 0
                ? 0
                : Math.Round((decimal)stalledNow.Count / depositCohortNow * 100, 1),
            DropOffPrevious = depositCohortPrev == 0
                ? 0
                : Math.Round((decimal)stalledPrev.Count / depositCohortPrev * 100, 1),
        };

        var stalledTrend = new List<StalledTrendPointDto>();
        foreach (var (ms, me, label) in TimeBuckets(fromUtc, toUtc))
        {
            // Cohort lấy từ toàn bộ booking để gồm cả ca huỷ sau khi trả cọc.
            var cohort = bookings.Where(b => b.CreatedAt >= ms && b.CreatedAt < me).ToList();
            stalledTrend.Add(new StalledTrendPointDto
            {
                Month = label,
                Stalled = cohort.Count(b => IsStalledAfterDeposit(b, now, SettledOf(b))),
                Converted = cohort.Count(b =>
                    b.Status is BookingStatus.Paid or BookingStatus.Ongoing or BookingStatus.Completed),
            });
        }

        // Hoàn tiền
        // Dùng wallet_transactions vì Booking.Refundamount chỉ là số luỹ kế, không có
        // mốc thời gian nên không quy được về kỳ.
        // Phải lọc Referencetable == booking: ví còn một loại giao dịch `Refund` khác gắn
        // Referencetable == withdrawal — tiền trả về ví GIA SƯ khi admin từ chối lệnh rút
        // (AdminPayoutService.cs). Đó không phải hoàn học phí cho phụ huynh, đếm vào đây sẽ
        // thổi phồng thẻ "Đã hoàn tiền" và kéo theo cả tỷ lệ hoàn trên tiền mặt.
        var refundTx = await context.Wallettransactions.AsNoTracking()
            .Where(t => t.Transactiontype == TransactionType.Refund
                        && t.Referencetable == ReferenceTable.Booking)
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
        foreach (var (ms, me, label) in TimeBuckets(fromUtc, toUtc))
        {
            var monthRefunds = refundTx.Where(t => t.Createdat >= ms && t.Createdat < me).ToList();
            refundTrend.Add(new RefundTrendPointDto
            {
                Month = label,
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
        var contacts = await LoadContactsAsync(ct);
        string? PayerContact(BookingFlat b) =>
            (b.ParentId ?? b.StudentId) is { } id ? contacts.GetValueOrDefault(id) : null;

        var subjectNames = await context.Subjects.AsNoTracking()
            .ToDictionaryAsync(s => s.Subjectid, s => s.Subjectname, ct);

        // Trước đây chỉ liệt kê booking CHƯA hoàn thành và cắt còn 30 dòng (bảng chẩn đoán
        // "ghi nhận sớm vs thực hiện"). Nay đổi thành danh sách booking đầy đủ kèm chỉ số doanh
        // thu để admin tra cứu trực tiếp — booking đã hoàn thành mới là nguồn doanh thu chắc
        // chắn nhất, không có lý do gì giấu đi.
        //
        // ─── Bảng này PHẢI cộng ra đúng thẻ đầu trang ─────────────────────────────────
        //
        // Ba điều kiện, thiếu một cái là chân bảng lệch với khối chia tiền ở đầu trang:
        //
        //   1. Cùng TẬP booking: cohort, LỌC THEO NGÀY TẠO trong kỳ — đúng biểu thức
        //      `soldInPeriod` của GetOverviewAsync. Trước đây bảng lấy cohort của MỌI thời
        //      điểm, không đọc bộ chọn thời gian, nên trang ghi "30 ngày qua" mà bảng liệt
        //      kê cả lịch từ những kỳ trước.
        //   2. ContractedFee phải là PHÍ THEO HỢP ĐỒNG, không phải số thực giữ — xem ngay dưới.
        //   3. Nhóm lịch chết-không-tiền cộng thêm ở dưới phải ra 0 đồng ở mọi cột tiền.
        //
        // ─── Vì sao ContractedFee không còn là `kept` ────────────────────────────────
        //
        // Bản cũ ghi `ContractedFee = isClosed ? kept : b.PlatformFee`, tức với khoá đã đóng
        // sổ thì cột này in ra số Tutora THỰC GIỮ. Hệ quả: cột mang tên "Doanh thu tạm tính"
        // nhưng với 42/53 dòng lại hiện đúng bằng cột "Đã thu được" bên cạnh, và tổng chân
        // bảng ra 1.192.500 trong khi thẻ đầu trang ghi 1.705.000 — chênh đúng 512.500, là
        // phần "Không thu được" đã bị cột này âm thầm nuốt mất.
        //
        // Nay in đúng phí hợp đồng. Khoảng chênh giữa hai cột giờ CÓ NGHĨA và đọc được bằng
        // cột Trạng thái ngay bên trái: lịch đang chạy thì đó là phần còn chờ dạy, lịch đã
        // huỷ thì đó là phần mất hẳn. Cộng cả hai loại chênh trên toàn bảng ra đúng hai lát
        // cam + đỏ của vành khuyên đầu trang.
        //
        // ─── Nhóm lịch chết mà không có đồng nào chạy qua ────────────────────────────
        //
        // Bảng dùng tập RỘNG HƠN cohort: cộng thêm lịch huỷ trước khi trả tiền, hoặc quá hạn
        // thanh toán. Cohort cố ý loại chúng vì đưa vào GMV là thổi phồng, nhưng giấu khỏi
        // bảng thì admin đi tìm "booking #304 tôi vừa huỷ đâu rồi" mà không có chỗ trả lời.
        //
        // Chúng KHÔNG làm lệch con số nào vì mọi cột tiền đều bằng 0 — và đó chính là lý do
        // ContractedFee của chúng phải bám cohort chứ không lấy thẳng `b.PlatformFee`: phí
        // hợp đồng của chúng vẫn có trong DB, in ra là thổi phồng chân bảng vượt thẻ đầu trang.
        //
        // Lịch còn ĐANG chờ (pending_tutor / accepted / pending_payment) vẫn nằm ngoài: chúng
        // chưa chết, chỉ là chưa tới lượt, đưa vào chỉ làm bảng doanh thu đầy dòng rỗng.
        var cohortIds = cohortBookings.Select(b => b.BookingId).ToHashSet();
        var tableBookings = cohortBookings
            .Concat(bookings.Where(b => !cohortIds.Contains(b.BookingId)
                                        && (b.CancelledAt != null
                                            || b.Status == BookingStatus.PaymentTimeout)))
            .Where(b => b.CreatedAt >= fromUtc && b.CreatedAt < toUtc)
            .ToList();

        var progress = tableBookings
            .Select(b =>
            {
                var settled = settledCount.TryGetValue(b.BookingId, out var c) ? c : 0;
                var isClosed = keptByBooking.TryGetValue(b.BookingId, out var kept);
                return new BookingProgressDto
                {
                    BookingId = b.BookingId,
                    ParentName = PayerName(b),
                    ParentContact = PayerContact(b),
                    TutorContact = b.TutorId == null ? null : contacts.GetValueOrDefault(b.TutorId),
                    TutorName = b.TutorId == null
                        ? "—"
                        : tutorNames.TryGetValue(b.TutorId, out var tn) ? tn
                        : parentNames.TryGetValue(b.TutorId, out var tu) ? tu
                        : b.TutorId,
                    Subject = b.SubjectId.HasValue && subjectNames.TryGetValue(b.SubjectId.Value, out var sn) ? sn : "—",
                    TotalSessions = b.TotalSessions,
                    DeliveredSessions = settled,
                    ContractedFee = cohortIds.Contains(b.BookingId) ? b.PlatformFee : 0,
                    RecognisedFee = isClosed ? kept : EarnedSoFar(b, toUtc, DeliveryOf(deliveries, b.BookingId)),
                    CashCollected = CashPaidIn(b),
                    RefundedAmount = refundedByBooking.TryGetValue(b.BookingId, out var rf) ? rf : 0,
                    Closed = isClosed,
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
                // Trả tiền rồi mà chưa học buổi nào: chưa qua buổi đầu nên KHÔNG có đồng nào
                // chín — toàn bộ phí sàn của khoá đều đang có nguy cơ.
                FeeAtRisk = neverStartedNow.Sum(b => UnearnedSoFar(b, now, Delivery.None)),
                CashHeld = neverStartedNow.Sum(b => b.FinalPrice),
            },
            StalledTrend = stalledTrend,
            Refunds = refunds,
            RefundTrend = refundTrend,
            BookingProgress = progress,
        };
    }
}
