using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MV.ApplicationLayer.Interfaces;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Configuration;

namespace MV.ApplicationLayer.Services;

/// <summary>
/// Relay bản ghi từ S3 (kho đệm Agora ghi vào) sang Google Drive.
///
/// Luồng mỗi file:
///   ① tải stream từ S3  →  ② upload thẳng lên Drive  →  ③ lưu link Drive vào DB
///   →  ④ xóa file S3 (để chỉ còn 1 chỗ lưu duy nhất là Drive).
/// File chỉ bị xóa khỏi S3 SAU KHI upload Drive thành công → an toàn, lỗi thì thử lại vòng sau.
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

        // Buổi học đã stop, còn S3 key = chưa relay lên Drive (s3key sẽ = null sau khi relay xong)
        var pending = await _context.ClassSessions
            .Where(s => s.Recordings3key != null)
            .OrderBy(s => s.Classsessionid)
            .Take(BatchSize)
            .Select(s => new
            {
                ClassSession = s,
                TutorName = s.Tutor != null && s.Tutor.Tutor != null ? s.Tutor.Tutor.Fullname : null,
                StudentName = s.Student != null ? s.Student.Fullname : null
            })
            .ToListAsync(ct);

        if (pending.Count == 0) return;

        using var s3 = CreateS3Client();

        foreach (var item in pending)
        {
            var session = item.ClassSession;
            var key = session.Recordings3key!;
            var fileName = $"session-{session.Classsessionid}.mp4";
            try
            {
                // ⓪ CHỐNG TRÙNG: nếu Drive đã có file (vòng trước upload xong nhưng
                //    SaveChanges lỗi / app restart giữa chừng khiến s3key chưa được xóa)
                //    → dùng lại fileId cũ, KHÔNG upload lần 2. Tên "session-{id}.mp4" cố định
                //    nên tra cứu theo tên là đủ; với scope drive.file chỉ thấy file do app này tạo.
                var fileId = await _drive.FindFileIdByNameAsync(fileName, ct);

                if (fileId == null)
                {
                    // Tổ chức theo Tutor/Student thay vì để phẳng — tạo lười (lazy), chỉ khi
                    // thực sự có buổi cần relay. Tên thư mục kèm id để không lẫn khi trùng tên.
                    var tutorFolderName = !string.IsNullOrWhiteSpace(item.TutorName)
                        ? $"{item.TutorName} ({session.Tutorid})" : session.Tutorid ?? "unknown-tutor";
                    var studentFolderName = !string.IsNullOrWhiteSpace(item.StudentName)
                        ? $"{item.StudentName} ({session.Studentid})" : session.Studentid ?? "unknown-student";
                    var folderId = await _drive.GetRecordingFolderAsync(tutorFolderName, studentFolderName, ct);

                    // ① tải file từ S3 (stream, không buffer toàn bộ vào RAM)
                    using var s3Obj = await s3.GetObjectAsync(_rec.StorageBucket, key, ct);

                    // ② upload thẳng lên Google Drive, vào đúng thư mục Tutor/Student
                    fileId = await _drive.UploadAsync(s3Obj.ResponseStream, fileName, "video/mp4", folderId, ct);
                }

                // ③ lưu link Drive + xóa s3key (= đánh dấu đã relay xong)
                session.Recordingurl = $"https://drive.google.com/file/d/{fileId}/view";
                session.Recordings3key = null;
                await _context.SaveChangesAsync(ct);

                // ④ xóa file đệm trên S3 (chỉ còn 1 chỗ = Drive)
                try
                {
                    await s3.DeleteObjectAsync(_rec.StorageBucket, key, ct);
                }
                catch (Exception delEx)
                {
                    _logger.LogWarning(delEx, "Đã relay nhưng xóa file S3 {Key} thất bại (không nghiêm trọng)", key);
                }

                _logger.LogInformation(
                    "Relayed recording: session={Session} → Drive fileId={FileId}", session.Classsessionid, fileId);
            }
            catch (Exception ex)
            {
                // Giữ nguyên status="stopped" → vòng sau tự thử lại (file S3 vẫn còn)
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
