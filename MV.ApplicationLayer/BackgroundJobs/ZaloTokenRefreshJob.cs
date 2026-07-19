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
    // Chạy mỗi 1h; PHẢI nhỏ hơn ProactiveRefreshThreshold (3h, xem ZaloOAService) —
    // nếu chu kỳ check dài hơn ngưỡng, có khoảng hở toán học: 1 lần check thấy TTL vừa
    // trên ngưỡng (bỏ qua, không log) rồi token chết hẳn trước lần check kế tiếp, khiến
    // bot dính -216 tới khi job tự bắt kịp (từng xảy ra thực tế: check lúc TTL=3h05p bỏ
    // qua, 6h sau mới check lại, token đã chết ~3h trước đó rồi).
    private readonly TimeSpan _interval = TimeSpan.FromHours(1);

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
