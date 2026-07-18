using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MV.ApplicationLayer.Interfaces;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Configuration;

namespace MV.ApplicationLayer.Services;

/// <summary>
/// Relay bản ghi từ Storj/S3 (kho đệm Agora ghi vào) sang Google Drive.
///
/// Trigger: buổi đã ghi hình (có recording_sid) + đã check-out nhưng chưa có recording_url.
/// KHÔNG dựa vào recording_s3key/fileList của lệnh stop (Agora hay trả rỗng rồi upload sau),
/// mà tự liệt kê Storj theo prefix recordings/{classSessionId}/ để tìm file.
///
/// Luồng mỗi buổi:
///   ① liệt kê Storj theo prefix → chọn .mp4  →  ② tải stream về  →  ③ upload lên Drive +
///   lưu link vào DB  →  ④ xóa file đệm trên Storj (để chỉ còn 1 chỗ lưu duy nhất là Drive).
/// url chỉ được set SAU KHI upload Drive thành công → an toàn, lỗi thì thử lại vòng sau.
/// </summary>
public class RecordingRelayService : IRecordingRelayService
{
    private const int BatchSize = 3; // số file xử lý mỗi vòng, tránh nghẽn

    private readonly IAppDbContext _context;
    private readonly IGoogleDriveService _drive;
    private readonly AgoraRecordingSettings _rec;
    private readonly ILogger<RecordingRelayService> _logger;

    public RecordingRelayService(
        IAppDbContext context,
        IGoogleDriveService drive,
        IOptions<AgoraRecordingSettings> rec,
        ILogger<RecordingRelayService> logger)
    {
        _context = context;
        _drive = drive;
        _rec = rec.Value;
        _logger = logger;
    }

    public async Task RelayPendingAsync(CancellationToken ct = default)
    {
        if (!_drive.Enabled) return;

        // Buổi ĐÃ ghi hình (có recording_sid) và ĐÃ check-out nhưng CHƯA có link Drive.
        // KHÔNG dựa vào recording_s3key nữa: Agora thường trả fileList RỖNG lúc stop rồi mới
        // upload file lên Storj vài giây sau (bất đồng bộ) → ta tự LIỆT KÊ theo prefix
        // recordings/{id}/ để tìm file. Giới hạn 7 ngày gần đây để không thử lại vô hạn với
        // buổi thật sự không có file nào (buổi quá ngắn / không có media).
        var cutoff = DateTime.UtcNow.AddDays(-7);
        var pending = await _context.ClassSessions
            .Where(s => s.Recordingsid != null
                        && s.Recordingurl == null
                        && s.Checkouttime != null
                        && s.Checkouttime >= cutoff)
            .OrderBy(s => s.Classsessionid)
            .Take(BatchSize)
            .ToListAsync(ct);

        if (pending.Count == 0) return;

        using var s3 = CreateS3Client();

        foreach (var session in pending)
        {
            try
            {
                // Tìm file Agora đã ghi cho buổi này trong Storj (thư mục recordings/{id}/).
                var prefix = $"recordings/{session.Classsessionid}/";
                var listing = await s3.ListObjectsV2Async(new ListObjectsV2Request
                {
                    BucketName = _rec.StorageBucket,
                    Prefix = prefix
                }, ct);

                var objects = listing.S3Objects ?? new List<S3Object>();
                // Ưu tiên file .mp4 để đưa lên Drive; fallback file đầu tiên nếu chưa có .mp4.
                var target = objects.FirstOrDefault(o => o.Key.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
                             ?? objects.FirstOrDefault();

                if (target == null)
                {
                    // Chưa thấy file: Agora có thể còn đang upload (thử lại vòng sau), hoặc buổi 0 file.
                    _logger.LogInformation(
                        "Chưa thấy file recording trên Storj cho buổi {Session} (prefix {Prefix}) — sẽ thử lại vòng sau.",
                        session.Classsessionid, prefix);
                    continue;
                }

                // ① tải file từ Storj (stream, không buffer toàn bộ vào RAM)
                using var s3Obj = await s3.GetObjectAsync(_rec.StorageBucket, target.Key, ct);

                // ② upload thẳng lên Google Drive
                var fileName = $"session-{session.Classsessionid}.mp4";
                var fileId = await _drive.UploadAsync(s3Obj.ResponseStream, fileName, "video/mp4", ct);

                // ③ lưu link Drive (= đánh dấu đã relay xong; url != null sẽ loại buổi này khỏi vòng sau)
                session.Recordingurl = $"https://drive.google.com/file/d/{fileId}/view";
                session.Recordings3key = null;
                await _context.SaveChangesAsync(ct);

                _logger.LogInformation(
                    "Relayed recording: session={Session} key={Key} → Drive fileId={FileId}",
                    session.Classsessionid, target.Key, fileId);

                // ④ xóa toàn bộ file đệm của buổi trên Storj (mp4 + m3u8 + ts...) — chỉ còn 1 chỗ = Drive
                foreach (var o in objects)
                {
                    try
                    {
                        await s3.DeleteObjectAsync(_rec.StorageBucket, o.Key, ct);
                    }
                    catch (Exception delEx)
                    {
                        _logger.LogWarning(delEx, "Đã relay nhưng xóa file Storj {Key} thất bại (không nghiêm trọng)", o.Key);
                    }
                }
            }
            catch (Exception ex)
            {
                // Lỗi tải/upload → giữ nguyên (url vẫn null) để vòng sau tự thử lại (file Storj vẫn còn).
                _logger.LogWarning(ex,
                    "Relay recording session {Session} thất bại, sẽ thử lại vòng sau", session.Classsessionid);
            }
        }
    }

    private IAmazonS3 CreateS3Client()
    {
        var creds = new BasicAWSCredentials(_rec.StorageAccessKey, _rec.StorageSecretKey);

        // S3-compatible (Backblaze B2, R2...) → dùng endpoint tùy chỉnh.
        // AWS SDK cần URL đầy đủ (có https) + AuthenticationRegion để ký SigV4 đúng.
        if (!string.IsNullOrWhiteSpace(_rec.StorageEndpoint))
        {
            var serviceUrl = _rec.StorageEndpoint!.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? _rec.StorageEndpoint!
                : $"https://{_rec.StorageEndpoint}";
            return new AmazonS3Client(creds, new AmazonS3Config
            {
                ServiceURL = serviceUrl,
                ForcePathStyle = true,
                AuthenticationRegion = string.IsNullOrWhiteSpace(_rec.StorageRegionName) ? "us-west-004" : _rec.StorageRegionName
            });
        }

        // AWS S3 thường → theo region string (vd "ap-southeast-1")
        var regionName = string.IsNullOrWhiteSpace(_rec.StorageRegionName) ? "ap-southeast-1" : _rec.StorageRegionName;
        return new AmazonS3Client(creds, RegionEndpoint.GetBySystemName(regionName));
    }
}
