using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MV.DomainLayer.Configuration;
using MV.ApplicationLayer.ServiceInterfaces;

namespace MV.ApplicationLayer.BackgroundJobs;

/// <summary>
/// Quét buổi học trôi qua giờ mà không ai vào lớp. Mặc định 30 phút — cửa sổ xử lý tính bằng
/// giờ nên không cần nhịp dày hơn. Hạ xuống qua cấu hình khi cần test end-to-end nhanh.
/// </summary>
public class AbandonedSessionJob : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AbandonedSessionJob> _logger;
    private readonly TimeSpan _interval;

    public AbandonedSessionJob(
        IServiceProvider serviceProvider,
        IOptions<AbandonedSessionSettings> settings,
        ILogger<AbandonedSessionJob> logger)
    {
        _serviceProvider = serviceProvider;
        _interval = TimeSpan.FromMinutes(settings.Value.ScanIntervalMinutes);
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("AbandonedSessionJob đã bắt đầu.");
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IAbandonedSessionService>();

                var touched = await service.ProcessAbandonedSessionsAsync(stoppingToken);
                if (touched > 0)
                    _logger.LogInformation("AbandonedSessionJob: đã xử lý {Count} buổi học bị bỏ quên.", touched);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi trong AbandonedSessionJob.");
            }

            await Task.Delay(_interval, stoppingToken);
        }

        _logger.LogInformation("AbandonedSessionJob đã dừng.");
    }
}
