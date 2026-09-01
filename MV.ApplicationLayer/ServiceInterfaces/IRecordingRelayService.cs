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

    /// <summary>
    /// Phục hồi các buổi mà lệnh dừng Cloud Recording lúc checkout đã thất bại (vd Agora tạm sập/mất
    /// mạng đúng lúc đó) — hỏi lại Agora xem file ghi hình có tồn tại không, nếu có thì gán
    /// Recordings3key như một lượt stop() thành công bình thường để RelayPendingAsync tự nhặt lên.
    /// Trả về số buổi phục hồi được.
    /// </summary>
    Task<int> RecoverStuckRecordingsAsync(CancellationToken ct = default);
}
