using System.Threading;
using System.Threading.Tasks;

namespace MV.ApplicationLayer.ServiceInterfaces;

/// <summary>
/// Chuyển (relay) các bản ghi Agora từ kho đệm S3 sang Google Drive:
/// tải từ S3 → upload Drive → lưu link → xóa file S3 (chỉ còn 1 chỗ = Drive).
/// </summary>
public interface IRecordingRelayService
{
    /// <summary>Xử lý các buổi học đã stop nhưng chưa relay lên Drive.</summary>
    Task RelayPendingAsync(CancellationToken ct = default);
}
