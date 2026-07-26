using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.ServiceInterfaces;

namespace MV.ApplicationLayer.BackgroundJobs;

/// <summary>
/// Worker nền tiêu thụ ITutorEmbedQueue: mỗi tutorId → gọi tutora-ai vector hoá gia sư.
/// </summary>
public class TutorEmbedWorker(
    IServiceProvider sp,
    ITutorEmbedQueue queue,
    ILogger<TutorEmbedWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        logger.LogInformation("TutorEmbedWorker bắt đầu — chờ sự kiện vector hoá gia sư.");

        await foreach (var tutorId in queue.DequeueAllAsync(ct))
        {
            try
            {
                using var scope = sp.CreateScope();
                var aiClient = scope.ServiceProvider.GetRequiredService<ITutorAiClient>();
                await aiClient.EmbedTutorAsync(tutorId, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "TutorEmbedWorker xử lý gia sư {TutorId} lỗi.", tutorId);
            }
        }
    }
}
