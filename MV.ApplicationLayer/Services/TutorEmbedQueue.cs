using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.ServiceInterfaces;
using System.Threading.Channels;

namespace MV.ApplicationLayer.Services;

/// <summary>
/// Cài đặt ITutorEmbedQueue bằng System.Threading.Channels (in-process, singleton).
/// </summary>
public class TutorEmbedQueue : ITutorEmbedQueue
{
    private readonly Channel<string> _channel;
    private readonly ILogger<TutorEmbedQueue> _logger;

    public TutorEmbedQueue(ILogger<TutorEmbedQueue> logger)
    {
        _logger = logger;
        _channel = Channel.CreateBounded<string>(new BoundedChannelOptions(1000)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });
    }

    public void Enqueue(string tutorId)
    {
        if (string.IsNullOrWhiteSpace(tutorId)) return;
        if (!_channel.Writer.TryWrite(tutorId))
            _logger.LogWarning("Không enqueue được embed gia sư {TutorId} (hàng đợi đầy).", tutorId);
    }

    public IAsyncEnumerable<string> DequeueAllAsync(CancellationToken cancellationToken)
        => _channel.Reader.ReadAllAsync(cancellationToken);
}
