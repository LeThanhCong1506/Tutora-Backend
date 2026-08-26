using Microsoft.EntityFrameworkCore;
using MV.ApplicationLayer.Interfaces;
using MV.DomainLayer.DTO.ResponseModel;

namespace MV.ApplicationLayer.Helpers;

/// <summary>
/// Đi chuỗi buổi liên kết (buổi bù/buổi phụ/buổi học lại — mọi loại đều tái dùng
/// Originalsessionid): lùi tới buổi gốc rồi đi tới hết mọi buổi con (BFS, không chỉ 1 bước) —
/// vì 1 buổi phụ/buổi bù sau đó vẫn có thể bị tranh chấp và sinh ra buổi học lại của chính nó,
/// tạo chuỗi dài hơn 2. Tách thành static helper (nhận thẳng IAppDbContext) thay vì để trong
/// ClassSessionService để DisputeService dùng lại được mà không phải phụ thuộc ngược
/// IClassSessionService (interface đó có 30+ method, không đáng để fake trong test chỉ vì 1 hàm).
/// </summary>
public static class ClassSessionRecordingChainHelper
{
    /// <summary>Chuỗi buổi chứa classSessionId, đã enrich trạng thái ghi hình từng buổi — KHÔNG set
    /// IsCurrent/StreamUrl (caller tự quyết theo actorId/mục đích gọi, xem
    /// ClassSessionService.GetClassSessionRecordingChainAsync/DisputeService.GetDisputeRecordingAsync).
    /// Null nếu không tìm thấy classSessionId.</summary>
    public static async Task<List<ClassSessionRecordingChainItem>?> GetChainAsync(IAppDbContext context, int classSessionId)
    {
        var target = await context.ClassSessions
            .Where(s => s.Classsessionid == classSessionId)
            .Select(s => new { s.Bookingid })
            .FirstOrDefaultAsync();
        if (target == null) return null;

        var ordered = await LoadOrderedChainAsync(context, classSessionId, target.Bookingid);

        var items = new List<ClassSessionRecordingChainItem>();
        for (var i = 0; i < ordered.Count; i++)
        {
            var s = ordered[i];
            var (status, url) = RecordingStatusResolver.Resolve(s.Recordingurl, s.Recordings3key, s.Recordingsid, s.Checkouttime.HasValue);
            items.Add(new ClassSessionRecordingChainItem
            {
                ClassSessionId = s.Classsessionid,
                Label = $"Buổi {i + 1}",
                ScheduledStart = s.Scheduledstart,
                Status = status,
                Available = url != null
            });
        }

        return items;
    }

    private static async Task<List<ChainSiblingRow>> LoadOrderedChainAsync(IAppDbContext context, int classSessionId, int? bookingId)
    {
        var siblings = await context.ClassSessions
            .Where(s => s.Bookingid == bookingId)
            .Select(s => new ChainSiblingRow(
                s.Classsessionid,
                s.Originalsessionid,
                s.Scheduledstart,
                s.Checkouttime,
                s.Recordingurl,
                s.Recordings3key,
                s.Recordingsid))
            .ToListAsync();

        var byId = siblings.ToDictionary(s => s.Classsessionid);

        var rootId = classSessionId;
        var backGuard = 0;
        while (byId.TryGetValue(rootId, out var node) && node.Originalsessionid.HasValue && backGuard++ < 20)
            rootId = node.Originalsessionid.Value;

        var chainIds = new List<int> { rootId };
        var frontier = new Queue<int>();
        frontier.Enqueue(rootId);
        while (frontier.Count > 0)
        {
            var current = frontier.Dequeue();
            foreach (var child in siblings.Where(s => s.Originalsessionid == current))
            {
                chainIds.Add(child.Classsessionid);
                frontier.Enqueue(child.Classsessionid);
            }
        }

        // Giữ đúng thứ tự cha → con từ BFS ở trên — KHÔNG sắp lại theo Scheduledstart, vì buổi phụ
        // (Iscontinuation) được tạo với giờ "ngay lúc ngắt + 1h" nên có thể có Scheduledstart SỚM
        // HƠN Scheduledstart gốc (đặc biệt nếu buổi gốc vốn được lên lịch xa trong tương lai) —
        // sắp theo giờ sẽ đẩy buổi phụ lên thành "Buổi 1", đảo ngược thứ tự thực học.
        return chainIds.Select(id => byId[id]).ToList();
    }

    private sealed record ChainSiblingRow(
        int Classsessionid,
        int? Originalsessionid,
        DateTime Scheduledstart,
        DateTime? Checkouttime,
        string? Recordingurl,
        string? Recordings3key,
        string? Recordingsid);
}
