using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.Interfaces;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel.Admin;
using MV.DomainLayer.Entities;
using MV.DomainLayer.Exceptions;
using MV.DomainLayer.Helpers;

namespace MV.ApplicationLayer.Services;

/// <summary>
/// Chi phí gọi Gemini. Nguồn dữ liệu là tutora-ai đẩy về — Google KHÔNG có API
/// đọc usage/spend theo API key, nên đây là số liệu duy nhất ta có.
/// </summary>
public class AdminAiUsageService(
    IAppDbContext context,
    IHttpClientFactory httpClientFactory,
    ILogger<AdminAiUsageService> logger) : IAdminAiUsageService
{
    /// <summary>Chặn lô quá lớn để 1 request hỏng không kéo sập bộ nhớ.</summary>
    private const int MaxBatchSize = 500;

    public async Task<int> IngestAsync(AiUsageIngestRequest request, CancellationToken ct = default)
    {
        var events = request.Events ?? [];
        if (events.Count == 0) return 0;

        if (events.Count > MaxBatchSize)
        {
            logger.LogWarning("Lô ai usage {Count} vượt {Max}, cắt bớt.", events.Count, MaxBatchSize);
            events = events.Take(MaxBatchSize).ToList();
        }

        var now = TimeZoneHelper.UtcNow;
        var rows = events
            // feature/model rỗng thì bản ghi vô nghĩa cho việc gom nhóm -> bỏ.
            .Where(e => !string.IsNullOrWhiteSpace(e.Feature) && !string.IsNullOrWhiteSpace(e.Model))
            .Select(e => new AiUsageEvent
            {
                Feature = e.Feature.Trim(),
                Model = e.Model.Trim(),
                Prompttokens = Math.Max(0, e.PromptTokens),
                Outputtokens = Math.Max(0, e.OutputTokens),
                Thoughtstokens = Math.Max(0, e.ThoughtsTokens),
                Cachedtokens = Math.Max(0, e.CachedTokens),
                Totaltokens = Math.Max(0, e.TotalTokens),
                Costusd = Math.Max(0m, e.CostUsd),
                Latencyms = e.LatencyMs,
                Success = e.Success,
                // Cắt bớt để 1 stacktrace dài không phình bảng.
                Error = Truncate(e.Error, 500),
                Createdat = e.CreatedAt.HasValue
                    ? DateTime.SpecifyKind(e.CreatedAt.Value, DateTimeKind.Utc)
                    : now,
            })
            .ToList();

        if (rows.Count == 0) return 0;

        context.AiUsageEvents.AddRange(rows);
        await context.SaveChangesAsync(ct);
        return rows.Count;
    }

    public async Task<AdminAiUsageResponse> GetUsageAsync(
        DateTime? from, DateTime? to, CancellationToken ct = default)
    {
        var (fromUtc, toUtc) = Normalise(from, to);
        var span = toUtc - fromUtc;
        var prevFrom = fromUtc - span;

        // Nạp 1 lần cả kỳ này lẫn kỳ trước rồi tách trong bộ nhớ — rẻ hơn 2 vòng DB,
        // và chỉ lấy đúng các cột cần cho thống kê.
        var rows = await context.AiUsageEvents
            .AsNoTracking()
            .Where(e => e.Createdat >= prevFrom && e.Createdat < toUtc)
            .Select(e => new UsageFlat(
                e.Feature,
                e.Model,
                e.Prompttokens,
                e.Outputtokens,
                e.Thoughtstokens,
                e.Cachedtokens,
                e.Totaltokens,
                e.Costusd,
                e.Latencyms,
                e.Success,
                e.Createdat))
            .ToListAsync(ct);

        var current = rows.Where(r => r.CreatedAt >= fromUtc).ToList();
        var previous = rows.Where(r => r.CreatedAt < fromUtc).ToList();

        var totals = new AdminAiUsageTotals
        {
            Calls = current.Count,
            TotalTokens = current.Sum(r => (long)r.TotalTokens),
            PromptTokens = current.Sum(r => (long)r.PromptTokens),
            OutputTokens = current.Sum(r => (long)r.OutputTokens),
            ThoughtsTokens = current.Sum(r => (long)r.ThoughtsTokens),
            CachedTokens = current.Sum(r => (long)r.CachedTokens),
            CostUsd = current.Sum(r => r.CostUsd),
            FailedCalls = current.Count(r => !r.Success),
            AvgLatencyMs = current.Any(r => r.LatencyMs.HasValue)
                ? (int)current.Where(r => r.LatencyMs.HasValue).Average(r => r.LatencyMs!.Value)
                : null,
            PrevCalls = previous.Count,
            PrevCostUsd = previous.Sum(r => r.CostUsd),
            PrevTotalTokens = previous.Sum(r => (long)r.TotalTokens),
        };

        var timeline = current
            .GroupBy(r => DateOnly.FromDateTime(r.CreatedAt))
            .OrderBy(g => g.Key)
            .Select(g => new AdminAiUsagePoint
            {
                Date = g.Key,
                Calls = g.Count(),
                TotalTokens = g.Sum(r => (long)r.TotalTokens),
                CostUsd = g.Sum(r => r.CostUsd),
            })
            .ToList();

        return new AdminAiUsageResponse
        {
            Totals = totals,
            Timeline = timeline,
            ByModel = Breakdown(current, r => r.Model, totals.CostUsd),
            ByFeature = Breakdown(current, r => r.Feature, totals.CostUsd),
        };
    }

    /// <summary>Gom nhóm theo model hoặc feature, sắp theo chi phí giảm dần.</summary>
    private static List<AdminAiUsageBreakdown> Breakdown(
        List<UsageFlat> rows, Func<UsageFlat, string> key, decimal totalCost)
        => rows
            .GroupBy(key)
            .Select(g =>
            {
                var cost = g.Sum(r => r.CostUsd);
                return new AdminAiUsageBreakdown
                {
                    Key = g.Key,
                    Calls = g.Count(),
                    TotalTokens = g.Sum(r => (long)r.TotalTokens),
                    CostUsd = cost,
                    FailedCalls = g.Count(r => !r.Success),
                    CostShare = totalCost > 0 ? Math.Round(cost / totalCost * 100m, 2) : 0m,
                };
            })
            .OrderByDescending(x => x.CostUsd)
            .ThenByDescending(x => x.Calls)
            .ToList();

    // Tỉ giá USD→VND
    // Google tính tiền bằng USD nên DB lưu USD; quy đổi chỉ phục vụ HIỂN THỊ.
    /// <summary>Dùng khi API tỉ giá không gọi được và admin chưa đặt tay.</summary>
    private const decimal FallbackRate = 26_000m;

    /// <summary>API tỉ giá công khai, không cần khoá.</summary>
    private const string RateApiUrl = "https://open.er-api.com/v6/latest/USD";

    /// <summary>Tỉ giá tự động đổi chậm -> cache để khỏi gọi ngoài mỗi lần mở trang.</summary>
    private static readonly TimeSpan RateCacheTtl = TimeSpan.FromHours(6);
    private static decimal? _cachedRate;
    private static DateTime _cachedAt = DateTime.MinValue;

    public async Task<AiUsageRateResponse> GetRateAsync(CancellationToken ct = default)
    {
        var cfg = await context.Systemconfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Configkey == AiCreditConfigKeys.UsdVndRate, ct);

        // Admin đã đặt tay -> tôn trọng, không gọi API ngoài.
        if (cfg is not null && decimal.TryParse(cfg.Configvalue, out var manual) && manual > 0)
        {
            return new AiUsageRateResponse
            {
                Rate = manual,
                IsManual = true,
                UpdatedAt = cfg.Updatedat,
                Source = "Admin nhập tay",
            };
        }

        var (rate, live) = await FetchRateAsync(ct);
        return new AiUsageRateResponse
        {
            Rate = rate,
            IsManual = false,
            UpdatedAt = live ? _cachedAt : null,
            Source = live ? "Tỉ giá thị trường (open.er-api.com)" : "Giá trị mặc định",
        };
    }

    public async Task<AiUsageRateResponse> SetRateAsync(
        decimal? rate, string? updatedByUserId, CancellationToken ct = default)
    {
        if (rate is <= 0)
            throw new BookingException(AiCreditErrorCodes.InvalidAmount, "Tỉ giá phải lớn hơn 0.", 400);

        var cfg = await context.Systemconfigs
            .FirstOrDefaultAsync(c => c.Configkey == AiCreditConfigKeys.UsdVndRate, ct);

        if (cfg is null)
        {
            cfg = new Systemconfig
            {
                Configkey = AiCreditConfigKeys.UsdVndRate,
                Description = "Tỉ giá USD→VND để hiển thị chi phí gọi Gemini.",
            };
            context.Systemconfigs.Add(cfg);
        }

        // null = xoá giá trị tay, quay lại lấy tự động.
        cfg.Configvalue = rate?.ToString(CultureInfo.InvariantCulture);
        cfg.Updatedby = updatedByUserId;
        cfg.Updatedat = TimeZoneHelper.UtcNow;
        await context.SaveChangesAsync(ct);

        return await GetRateAsync(ct);
    }

    /// <summary>Lấy tỉ giá thị trường. Trả (tỉ giá, có phải số liệu thật không).</summary>
    private async Task<(decimal Rate, bool Live)> FetchRateAsync(CancellationToken ct)
    {
        if (_cachedRate is > 0 && TimeZoneHelper.UtcNow - _cachedAt < RateCacheTtl)
            return (_cachedRate.Value, true);

        try
        {
            using var http = httpClientFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(8);

            using var doc = JsonDocument.Parse(await http.GetStringAsync(RateApiUrl, ct));
            if (doc.RootElement.TryGetProperty("rates", out var rates)
                && rates.TryGetProperty("VND", out var vnd)
                && vnd.TryGetDecimal(out var value)
                && value > 0)
            {
                _cachedRate = decimal.Round(value, 2);
                _cachedAt = TimeZoneHelper.UtcNow;
                return (_cachedRate.Value, true);
            }
        }
        catch (Exception ex)
        {
            // Hiển thị tiền không được chặn cả trang -> lùi về giá trị mặc định.
            logger.LogWarning(ex, "Không lấy được tỉ giá USD/VND, dùng mặc định.");
        }

        return (_cachedRate ?? FallbackRate, false);
    }

    private static (DateTime FromUtc, DateTime ToUtc) Normalise(DateTime? from, DateTime? to)
    {
        var now = TimeZoneHelper.UtcNow;

        // FE gửi ngày trần ('2026-08-31') -> parse ra 00:00, tức ĐẦU ngày. Nếu lấy
        // thẳng làm mốc kết thúc thì mọi sự kiện trong chính ngày hôm đó bị loại
        // (khoảng lọc là nửa mở [from, to)) -> trang luôn rỗng. Vì vậy khi `to` rơi
        // đúng nửa đêm, đẩy sang đầu ngày kế tiếp để trọn ngày được tính.
        var toUtc = to.HasValue
            ? DateTime.SpecifyKind(to.Value, DateTimeKind.Utc)
            : now;
        if (to.HasValue && toUtc.TimeOfDay == TimeSpan.Zero)
            toUtc = toUtc.AddDays(1);

        // Mặc định 30 ngày: chi phí AI cần nhìn gần, khác báo cáo doanh thu nhìn 12 tháng.
        var fromUtc = from.HasValue
            ? DateTime.SpecifyKind(from.Value, DateTimeKind.Utc)
            : toUtc.AddDays(-30);

        return (fromUtc, toUtc);
    }

    private static string? Truncate(string? value, int max)
        => string.IsNullOrEmpty(value) || value.Length <= max ? value : value[..max];

    private record UsageFlat(
        string Feature,
        string Model,
        int PromptTokens,
        int OutputTokens,
        int ThoughtsTokens,
        int CachedTokens,
        int TotalTokens,
        decimal CostUsd,
        int? LatencyMs,
        bool Success,
        DateTime CreatedAt);
}
