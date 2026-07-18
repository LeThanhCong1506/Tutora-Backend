using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MV.ApplicationLayer.ServiceInterfaces;

/// <summary>Thông tin trả về khi bắt đầu record — cần lưu lại để stop sau này.</summary>
/// <param name="ResourceId">resourceId từ bước acquire.</param>
/// <param name="Sid">sid từ bước start.</param>
public record CloudRecordingHandle(string ResourceId, string Sid);

/// <summary>Kết quả khi dừng record.</summary>
/// <param name="ResourceId">resourceId của phiên record.</param>
/// <param name="Sid">sid của phiên record.</param>
/// <param name="FileNames">Danh sách tên file Agora đã ghi vào kho (S3...).</param>
/// <param name="PlaybackUrl">Link xem lại (nếu đã cấu hình PublicUrlBase), ưu tiên file .mp4.</param>
public record CloudRecordingResult(
    string ResourceId,
    string Sid,
    IReadOnlyList<string> FileNames,
    string? PlaybackUrl);

/// <summary>
/// Agora Cloud Recording — gọi REST API acquire/start/stop để ghi lại buổi học lên cloud storage.
/// Docs: https://docs.agora.io/en/cloud-recording/reference/rest-api
/// </summary>
public interface ICloudRecordingService
{
    /// <summary>True nếu tính năng đã bật (AgoraRecording:Enabled). Dùng để bỏ qua khi chưa cấu hình.</summary>
    bool Enabled { get; }

    /// <summary>
    /// Bắt đầu record cho một buổi học. <paramref name="channel"/> PHẢI là channel mà client
    /// đang join (xem <c>AgoraChannelName.ForSession</c> — channel chung theo booking).
    /// classSessionId chỉ dùng đặt tên thư mục file + log. Trả resourceId + sid.
    /// </summary>
    Task<CloudRecordingHandle> StartAsync(int classSessionId, string channel, CancellationToken ct = default);

    /// <summary>Dừng record. Cần đúng channel lúc start + resourceId + sid nhận được lúc start.</summary>
    Task<CloudRecordingResult> StopAsync(int classSessionId, string channel, string resourceId, string sid, CancellationToken ct = default);
}
