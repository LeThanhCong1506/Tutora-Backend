using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.ServiceInterfaces;

namespace MV.ApplicationLayer.BackgroundJobs;

/// <summary>
/// Chủ động refresh Zalo OA access token trước khi hết hạn.
///
/// Vì sao cần: access token TTL ~23h; refresh trước đây CHỈ chạy lazy khi backend tự
/// gửi Zalo (OTP/ZNS). Nhưng bên gửi tin thường xuyên là BOT, mà bot chỉ ĐỌC token từ
/// Redis, không trigger refresh. Nếu backend không phát sinh request Zalo nào trong 23h,
/// token hết hạn -> Redis xoá -> bot gửi tin bị lỗi -216. Job này lấp mảnh thiếu đó.
///
/// Backend vẫn là nơi DUY NHẤT refresh (bot không refresh) -> không phá cơ chế rotating
/// refresh token của Zalo. EnsureFreshTokenAsync tự bỏ qua nếu token còn hạn thoải mái.
/// </summary>
public class ZaloTokenRefreshJob(IServiceProvider sp, ILogger<ZaloTokenRefreshJob> logger) : BackgroundService
{
    // Chạy mỗi 6h; access token TTL ~23h, ngưỡng proactive refresh 3h -> luôn refresh
    // trước khi hết, với thừa số an toàn (kể cả bỏ lỡ 1-2 lần chạy do restart).
    private readonly TimeSpan _interval = TimeSpan.FromHours(6);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        logger.LogInformation("ZaloTokenRefreshJob bắt đầu chạy.");

        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var scope = sp.CreateScope();
                var zaloOa = scope.ServiceProvider.GetRequiredService<IZaloOAService>();
                await zaloOa.EnsureFreshTokenAsync();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Lỗi khi chủ động refresh Zalo OA token.");
            }

            await Task.Delay(_interval, ct);
        }
    }
}
