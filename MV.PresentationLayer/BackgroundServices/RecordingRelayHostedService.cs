using MV.ApplicationLayer.ServiceInterfaces;

namespace MV.PresentationLayer.BackgroundServices;

/// <summary>
/// Chạy nền: định kỳ relay các bản ghi Agora từ S3 sang Google Drive.
///
/// Tách khỏi luồng check-out vì file 2-4h nặng vài GB — tải + upload mất vài phút,
/// không nên chặn API. Job chạy mỗi 60 giây, mỗi vòng xử lý vài file.
/// </summary>
public class RecordingRelayHostedService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RecordingRelayHostedService> _logger;

    public RecordingRelayHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<RecordingRelayHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var relay = scope.ServiceProvider.GetRequiredService<IRecordingRelayService>();

                var recoveredCount = await relay.RecoverStuckRecordingsAsync(stoppingToken);
                if (recoveredCount > 0)
                    _logger.LogInformation("RecordingRelayHostedService: phục hồi {Count} bản ghi bị treo do stop() thất bại lúc checkout.", recoveredCount);

                await relay.RelayPendingAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi trong RecordingRelayHostedService");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }
}
