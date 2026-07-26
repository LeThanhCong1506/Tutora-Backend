namespace MV.ApplicationLayer.ServiceInterfaces;

/// <summary>
/// Hàng đợi nền để vector hoá gia sư (event-driven). Write path (duyệt hồ sơ, đổi bio/môn/
/// giá) chỉ ENQUEUE tutorId rồi trả ngay
/// </summary>
public interface ITutorEmbedQueue
{
    /// <summary>Đặt lịch vector hoá 1 gia sư. Không block, không throw.</summary>
    void Enqueue(string tutorId);

    /// <summary>Worker đọc lần lượt tutorId chờ xử lý (chặn đến khi có item).</summary>
    IAsyncEnumerable<string> DequeueAllAsync(CancellationToken cancellationToken);
}
