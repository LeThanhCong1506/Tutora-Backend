using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.ResponseModel.Admin;

namespace MV.ApplicationLayer.Services;

public partial class AdminRevenueAnalyticsService
{
    public async Task<AdminAiRevenueResponse> GetAiRevenueAsync(
        DateTime? from, DateTime? to, int top, CancellationToken ct = default)
    {
        var (fromUtc, toUtc) = Normalise(from, to);
        var (prevFrom, prevTo) = PreviousPeriod(fromUtc, toUtc);

        var purchases = await context.PaymentTransactions
            .AsNoTracking()
            .Where(t => t.Purpose == PaymentTransactionPurpose.AiCreditPurchase
                        && t.Status == PaymentTransactionStatus.Succeeded)
            .Select(t => new
            {
                t.Amount,
                t.AiCreditPackageid,
                t.AiCreditUserid,
                When = t.Paidat ?? t.Createdat,
            })
            .ToListAsync(ct);

        var packages = await context.AiCreditPackages.AsNoTracking()
            .Select(p => new { p.Packageid, p.Name, p.Price, p.Creditamount })
            .ToListAsync(ct);

        // Ledger CHỈ chứa grant/purchase — theo thiết kế không ghi mỗi lượt hỏi
        // (xem AiCreditService.SpendAsync). Số liệu tiêu thụ lấy từ ai_usage_monthly,
        // số dư lấy từ users.
        var creditTx = await context.AiCreditTransactions.AsNoTracking()
            .Where(t => t.Amount > 0)
            .Select(t => new { t.Userid, t.Amount, t.Source, t.Createdat })
            .ToListAsync(ct);

        var usage = await context.AiUsageMonthly.AsNoTracking()
            .Select(u => new { u.Userid, u.Period, u.Usedcount })
            .ToListAsync(ct);

        var users = await context.Users.AsNoTracking()
            .Select(u => new
            {
                u.Userid,
                Name = u.Fullname ?? u.Userid,
                u.Primaryrole,
                u.AiCreditsBalance,
            })
            .ToListAsync(ct);
        var userNames = users.ToDictionary(u => u.Userid, u => new { u.Name, u.Primaryrole });

        var inPeriod = purchases.Where(p => p.When >= fromUtc && p.When < toUtc).ToList();
        var inPrev = purchases.Where(p => p.When >= prevFrom && p.When < prevTo).ToList();

        // Luỹ kế toàn hệ thống. "Đã cấp" đọc thẳng ledger — đó là sổ gốc của mọi
        // khoản cấp phát (tặng khi đăng ký, tặng theo lịch học, mua gói).
        var creditsSold = creditTx.Sum(t => t.Amount);
        var creditsConsumed = usage.Sum(u => u.Usedcount);
        var outstanding = users.Sum(u => u.AiCreditsBalance);

        // Ba số này KHÔNG nhất thiết khép kín (cấp = dùng + còn lại). Chênh lệch là
        // số lượt đã tiêu TRƯỚC khi bảng ai_usage_monthly ra đời — lúc đó SpendAsync
        // chỉ trừ số dư mà không ghi lại ở đâu cả. Phần này không khôi phục được
        // ngoài số suy từ chat_histories.
        var reconciliationGap = creditsSold - creditsConsumed - outstanding;
        if (Math.Abs(reconciliationGap) > 0)
        {
            logger.LogInformation(
                "AI credit lệch {Gap} lượt (cấp {Sold} − dùng {Used} − còn {Left}) — lượt tiêu trước khi có bảng thống kê.",
                reconciliationGap, creditsSold, creditsConsumed, outstanding);
        }

        // Nhóm ĐÃ KÍCH HOẠT: từng hỏi ít nhất một lượt. Mọi tài khoản đều được tặng
        // lượt Free nên nếu đo cường độ sử dụng trên toàn bộ, mẫu số bị pha loãng
        // bởi người chưa bao giờ mở tính năng.
        var usedByUser = usage
            .GroupBy(u => u.Userid)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Usedcount));
        var activatedIds = usedByUser
            .Where(kv => kv.Value > 0)
            .Select(kv => kv.Key)
            .ToHashSet();

        var activatedConsumed = activatedIds.Sum(id => usedByUser[id]);
        var activatedGranted = creditTx
            .Where(t => activatedIds.Contains(t.Userid))
            .Sum(t => t.Amount);

        // Mẫu số của tỷ lệ kích hoạt: tài khoản THỰC SỰ được cấp lượt (có số dư
        // hoặc đã từng hỏi). Đếm cả bảng users sẽ tính luôn tài khoản chưa từng
        // nhận gì, làm tỷ lệ thấp giả tạo.
        var grantedUserCount = users.Count(u =>
            u.AiCreditsBalance > 0 || activatedIds.Contains(u.Userid));

        var packageRows = packages
            .Select(p => new AiPackageDto
            {
                PackageId = p.Packageid,
                Name = p.Name,
                Price = p.Price,
                CreditAmount = p.Creditamount,
                UnitsSold = inPeriod.Count(x => x.AiCreditPackageid == p.Packageid),
                Revenue = inPeriod.Where(x => x.AiCreditPackageid == p.Packageid).Sum(x => x.Amount),
            })
            .OrderByDescending(p => p.Revenue)
            .ToList();

        // Lượt cấp và lượt dùng theo tháng.
        //
        // Mốc "cấp" lấy từ ledger. Các dòng tặng Free cho tài khoản đăng ký trước
        // khi tính năng AI ra mắt được ghi mốc = ngày ra mắt (xem
        // scripts/sql/fix_grant_created_at.sql), vì đó mới là lúc họ thực sự nhận
        // được lượt. Nếu ledger ghi mốc chạy script thì biểu đồ sẽ ra nghịch lý
        // kiểu "tháng này cấp 0 nhưng vẫn dùng được".
        var flow = new List<AiCreditFlowDto>();
        foreach (var (ms, me, label) in TimeBuckets(fromUtc, toUtc))
        {
            var msDate = DateOnly.FromDateTime(ms);
            flow.Add(new AiCreditFlowDto
            {
                Month = label,
                // Chỉ đếm lượt cấp cho nhóm đã từng hỏi bài — xem ghi chú ở
                // AiCreditFlowDto.Granted về lý do lọc.
                Granted = creditTx
                    .Where(t => activatedIds.Contains(t.Userid)
                                && t.Createdat >= ms && t.Createdat < me)
                    .Sum(t => t.Amount),
                Consumed = usage.Where(u => u.Period == msDate).Sum(u => u.Usedcount),
            });
        }

        // Top người MUA gói AI — sắp theo số tiền đã trả, không theo lượt tiêu thụ.
        //
        // Lấy từ `inPeriod`, không phải toàn bộ `purchases`. Trước đây bảng này gom mọi lần
        // mua từ trước tới nay, nên nó có thể liệt kê người không mua gì trong kỳ, ngay bên
        // dưới thẻ "Doanh thu AI kỳ này" vốn chỉ tính trong kỳ — hai khối cạnh nhau nói về
        // hai khoảng thời gian khác nhau mà không có gì báo.
        var topUsers = inPeriod
            .Where(p => !string.IsNullOrEmpty(p.AiCreditUserid))
            .GroupBy(p => p.AiCreditUserid!)
            .Select(g =>
            {
                var info = userNames.TryGetValue(g.Key, out var u) ? u : null;
                return new AiTopUserDto
                {
                    UserId = g.Key,
                    UserName = info?.Name ?? g.Key,
                    Role = info?.Primaryrole ?? "—",
                    CreditsConsumed = usage.Where(x => x.Userid == g.Key).Sum(x => x.Usedcount),
                    CreditsPurchased = creditTx
                        .Where(t => t.Userid == g.Key && t.Source == AiCreditSource.Purchase)
                        .Sum(t => t.Amount),
                    AmountPaid = g.Sum(p => p.Amount),
                };
            })
            .OrderByDescending(u => u.AmountPaid)
            .Take(Math.Max(top, 1))
            .ToList();

        var trend = new List<RevenueTrendPointDto>();
        foreach (var (ms, me, label) in TimeBuckets(fromUtc, toUtc))
        {
            trend.Add(new RevenueTrendPointDto
            {
                Month = label,
                AiRevenue = purchases.Where(p => p.When >= ms && p.When < me).Sum(p => p.Amount),
            });
        }

        return new AdminAiRevenueResponse
        {
            Summary = new AiSummaryDto
            {
                Revenue = inPeriod.Sum(p => p.Amount),
                RevenuePrevious = inPrev.Sum(p => p.Amount),
                PackagesSold = inPeriod.Count(p => p.AiCreditPackageid != null),
                PackagesSoldPrevious = inPrev.Count(p => p.AiCreditPackageid != null),
                CreditsSold = creditsSold,
                CreditsConsumed = creditsConsumed,
                CreditsOutstanding = outstanding,
                TotalUsers = grantedUserCount,
                ActivatedUsers = activatedIds.Count,
                ActivatedCreditsGranted = activatedGranted,
                ActivatedCreditsConsumed = activatedConsumed,
            },
            Packages = packageRows,
            CreditFlow = flow,
            TopUsers = topUsers,
            Trend = trend,
        };
    }
}
