using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MV.ApplicationLayer.Interfaces;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Configuration;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.Entities;
using MV.DomainLayer.Helpers;

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

    // Agora acquire() cấp resourceExpiredHour=24 (CloudRecordingService.StartInternalAsync) — quá mốc
    // này thì resourceId/sid không còn hỏi lại được nữa, ngừng thử để khỏi hầu Agora vô ích.
    private const int RecoveryWindowHours = 20;

    private readonly IAppDbContext _context;
    private readonly IGoogleDriveService _drive;
    private readonly ICloudRecordingService _cloudRecording;
    private readonly INotificationService _notificationService;
    private readonly IBackgroundJobClient _backgroundJobClient;
    private readonly IGeminiVideoAnalysisService _gemini;
    private readonly AgoraRecordingSettings _rec;
    private readonly ILogger<RecordingRelayService> _logger;

    public RecordingRelayService(
        IAppDbContext context,
        IGoogleDriveService drive,
        ICloudRecordingService cloudRecording,
        INotificationService notificationService,
        IBackgroundJobClient backgroundJobClient,
        IGeminiVideoAnalysisService gemini,
        IOptions<AgoraRecordingSettings> rec,
        ILogger<RecordingRelayService> logger)
    {
        _context = context;
        _drive = drive;
        _cloudRecording = cloudRecording;
        _notificationService = notificationService;
        _backgroundJobClient = backgroundJobClient;
        _gemini = gemini;
        _rec = rec.Value;
        _logger = logger;
    }

    /// <summary>
    /// Buổi đã checkout nhưng TryStopRecordingAsync (ClassSessionService.M3.Attendance) không lấy
    /// được file — thường vì lệnh stop() gọi lúc đó bị lỗi (Agora tạm sập/mất mạng, hoặc recorder đã
    /// tự thoát theo maxIdleTime trước khi mình kịp gọi stop). resourceId/sid vẫn còn nằm trên
    /// ClassSession nên thử phục hồi trong <see cref="RecoveryWindowHours"/> giờ kể từ checkout.
    /// Có file thì gán Recordings3key y như một lượt stop() thành công, để RelayPendingAsync bên
    /// dưới tự nhặt lên trong lượt quét kế tiếp — không cần đường dẫn xử lý riêng.
    /// </summary>
    public async Task<int> RecoverStuckRecordingsAsync(CancellationToken ct = default)
    {
        if (!_cloudRecording.Enabled) return 0;

        var now = TimeZoneHelper.UtcNow;
        var cutoff = now.AddMinutes(-2); // tránh đua với 1 lượt checkout vừa commit, còn chưa kịp có kết quả stop()
        var expiryFloor = now.AddHours(-RecoveryWindowHours);

        var stuck = await _context.ClassSessions
            .Where(s => s.Checkouttime != null
                     && s.Checkouttime <= cutoff
                     && s.Checkouttime >= expiryFloor
                     && s.Recordingresourceid != null
                     && s.Recordingsid != null
                     && s.Recordingurl == null
                     && s.Recordings3key == null)
            .OrderBy(s => s.Classsessionid)
            .Take(BatchSize)
            .ToListAsync(ct);

        if (stuck.Count == 0) return 0;

        using var s3 = CreateS3Client();
        var recoveredCount = 0;
        foreach (var session in stuck)
        {
            try
            {
                var mp4Key = await FindRecordingFileKeyAsync(
                    s3, session.Classsessionid, session.Recordingresourceid!, session.Recordingsid!, ct);

                // Chưa tìm ra file (Agora chưa xử lý xong, hoặc thật sự chưa ghi được gì) — thử lại
                // vòng sau, vẫn còn trong RecoveryWindowHours nên không vội kết luận hỏng.
                if (mp4Key == null) continue;

                session.Recordings3key = mp4Key;
                await _context.SaveChangesAsync(ct);
                recoveredCount++;

                _logger.LogInformation(
                    "Phục hồi được bản ghi cho classSession {ClassSessionId} (resourceId={ResourceId}) sau khi stop() thất bại lúc checkout.",
                    session.Classsessionid, session.Recordingresourceid);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Không phục hồi được bản ghi cho classSession {ClassSessionId}, thử lại vòng sau.",
                    session.Classsessionid);
            }
        }

        return recoveredCount;
    }

    /// <summary>
    /// ① Hỏi Agora control-plane (query — không phải stop, xem RecoverStuckRecordingsAsync) trước:
    /// nhanh, ra đúng tên file nếu resourceId/sid vẫn còn "sống" trên Agora.
    /// ② Agora trả "failed to find worker" (control-plane đã dọn resource, thường do recorder tự
    /// thoát theo maxIdleTime) KHÔNG có nghĩa file đã mất — Agora tải file lên storage độc lập với
    /// control-plane, nên phải tự dò thẳng bucket theo đúng prefix cấu hình lúc acquire
    /// (CloudRecordingService.BuildStorageConfig: "recordings/{classSessionId}/...") trước khi kết
    /// luận hỏng. Bug thật đã gặp ở buổi 1062: query() báo 404 nhưng file .mp4 445MB vẫn nguyên
    /// trong Storj, tra bằng tay mới lôi được ra — bước ② tồn tại để không cần tra tay nữa.
    /// </summary>
    private async Task<string?> FindRecordingFileKeyAsync(
        IAmazonS3 s3, int classSessionId, string resourceId, string sid, CancellationToken ct)
    {
        try
        {
            var result = await _cloudRecording.QueryAsync(resourceId, sid, ct);
            var mp4FromAgora = result.FileNames.FirstOrDefault(f => f.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase));
            if (mp4FromAgora != null) return mp4FromAgora;
        }
        catch (Exception ex)
        {
            _logger.LogInformation(ex,
                "Agora query() không trả được trạng thái cho classSession {ClassSessionId}, thử dò thẳng kho lưu trữ.",
                classSessionId);
        }

        var listing = await s3.ListObjectsV2Async(new ListObjectsV2Request
        {
            BucketName = _rec.StorageBucket,
            Prefix = $"recordings/{classSessionId}/",
        }, ct);

        return listing.S3Objects?
            .Where(o => o.Key.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) && o.Key.Contains(sid))
            .OrderByDescending(o => o.Size)
            .Select(o => o.Key)
            .FirstOrDefault();
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
                StudentName = s.Student != null ? s.Student.Fullname : null,
                // Studentid là mã hồ sơ ("STU-xxxx"), không phải Userid để đăng nhập/nhận thông báo khi
                // tài khoản do phụ huynh tạo — khi đó Linkeduserid mới là Userid thật (tự đăng ký thì
                // Linkeduserid = Studentid = chính họ, nên fallback về Studentid vẫn đúng).
                StudentNotifyUserId = s.Student != null && !string.IsNullOrEmpty(s.Student.Linkeduserid)
                    ? s.Student.Linkeduserid
                    : s.Studentid,
                ParentUserId = s.Student != null ? s.Student.Parentid : null
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

                // Video vừa ghi xong không thể xem/dùng AI ngay (RTC record → S3 → Drive mất vài phút) —
                // báo chủ động cho học sinh/gia sư khi đã thật sự sẵn sàng, khỏi phải tự bấm kiểm tra lại.
                await NotifyRecordingReadyAsync(session.Classsessionid, item.StudentNotifyUserId, session.Tutorid, item.ParentUserId);

                // Làm nóng cache GeminiFileUri ngay từ đây (tải+transcode+upload lên Gemini) — để tới lúc
                // học sinh/gia sư bấm tóm tắt/điền báo cáo, phần tốn thời gian nhất đã chạy xong từ trước.
                // Best-effort, chạy queue "bulk" nên không tranh worker với job tương tác trực tiếp.
                _backgroundJobClient.Enqueue<IClassSessionVideoAiService>(s => s.PrewarmGeminiFileAsync(session.Classsessionid));
            }
            catch (Amazon.S3.Model.NoSuchKeyException)
            {
                // File nguồn trên S3/Storj không còn tồn tại (xóa thủ công, hết TTL, hoặc key sai
                // ngay từ đầu) — retry mãi mãi vô ích, và vì RelayPendingAsync luôn lấy theo thứ tự
                // Classsessionid tăng dần (Take(BatchSize)), một buổi kẹt kiểu này sẽ chiếm trọn suất
                // của batch ở MỌI vòng, chặn luôn các buổi mới hơn phía sau không bao giờ được thử.
                // Coi là thất bại vĩnh viễn: xóa s3key (không set url) để RecordingStatusResolver trả
                // "failed" thay vì "processing" treo mãi, đồng thời nhường chỗ batch cho buổi sau.
                session.Recordings3key = null;
                await _context.SaveChangesAsync(ct);
                _logger.LogError(
                    "Relay recording session {Session} thất bại VĨNH VIỄN: S3 key {Key} không tồn tại. Đã đánh dấu failed, không thử lại nữa.",
                    session.Classsessionid, key);
            }
            catch (Exception ex)
            {
                // Giữ nguyên status="stopped" → vòng sau tự thử lại (file S3 vẫn còn)
                _logger.LogWarning(ex,
                    "Relay recording session {Session} thất bại, sẽ thử lại vòng sau", session.Classsessionid);
            }
        }

        await RelayPendingAudioAsync(ct);
    }

    /// <summary>Forward file audio-only (recorder song song, xem CloudRecordingService.StartAudioAsync)
    /// thẳng lên Gemini File API rồi xoá khỏi S3 — KHÔNG relay lên Drive (không có nhu cầu phát lại audio
    /// riêng, video mix đã lo phần đó). Kết quả ghi thẳng vào 1 ClassSessionAiJob (Prewarm) để
    /// EnsureUploadedFileAsync coi như cache đã "ấm" — tới lúc có job tóm tắt/điền báo cáo thật, khỏi
    /// phải tải video + ffmpeg tách audio (nhánh cũ) nữa. Lỗi ở đây chỉ log — nhánh cũ vẫn tự chạy được
    /// như một fallback.</summary>
    /// <summary>Xử lý file audio-only đúng như video: tải từ S3 → relay lên Drive lưu vĩnh viễn (chống
    /// trùng theo tên cố định, cùng thư mục Tutor/Student với video) → xoá bản đệm S3. Khác video ở chỗ
    /// còn forward thêm 1 bản lên Gemini File API để cache sẵn cho pipeline AI (tóm tắt/điền báo cáo),
    /// vì đây vẫn là mục đích chính của recorder audio-only.</summary>
    private async Task RelayPendingAudioAsync(CancellationToken ct)
    {
        var pending = await _context.ClassSessions
            .Where(s => s.Audiorecordings3key != null)
            .OrderBy(s => s.Classsessionid)
            .Take(BatchSize)
            .Select(s => new
            {
                ClassSession = s,
                TutorName = s.Tutor != null && s.Tutor.Tutor != null ? s.Tutor.Tutor.Fullname : null,
                StudentName = s.Student != null ? s.Student.Fullname : null,
            })
            .ToListAsync(ct);

        if (pending.Count == 0) return;

        using var s3 = CreateS3Client();

        foreach (var item in pending)
        {
            var session = item.ClassSession;
            var key = session.Audiorecordings3key!;
            var fileName = $"session-{session.Classsessionid}-audio.mp3";
            string? tempPath = null;
            try
            {
                // Tải về file tạm (đọc lại được nhiều lần) thay vì đọc thẳng ResponseStream — cần dùng
                // nội dung này 2 lần (upload Gemini + upload Drive), stream S3 chỉ đọc được 1 lần.
                long contentLength;
                tempPath = Path.Combine(Path.GetTempPath(), $"class-session-audio-relay-{Guid.NewGuid():N}.mp3");
                using (var s3Obj = await s3.GetObjectAsync(_rec.StorageBucket, key, ct))
                {
                    contentLength = s3Obj.ContentLength;
                    await using var fileStream = File.Create(tempPath);
                    await s3Obj.ResponseStream.CopyToAsync(fileStream, ct);
                }

                // ① forward lên Gemini File API — cache cho pipeline AI, giống PrewarmGeminiFileAsync.
                MV.DomainLayer.DTO.ResponseModel.GeminiUploadedFile uploaded;
                await using (var geminiStream = File.OpenRead(tempPath))
                {
                    uploaded = await _gemini.UploadVideoAsync(geminiStream, contentLength, "audio/mp4", fileName, ct);
                }
                await _gemini.WaitForFileActiveAsync(uploaded.Name, ct);

                // ② relay lên Drive lưu vĩnh viễn — cùng cấu trúc thư mục Tutor/Student với video, chống
                // upload trùng theo tên cố định giống hệt logic video ở RelayPendingAsync phía trên.
                var driveFileId = await _drive.FindFileIdByNameAsync(fileName, ct);
                if (driveFileId == null)
                {
                    var tutorFolderName = !string.IsNullOrWhiteSpace(item.TutorName)
                        ? $"{item.TutorName} ({session.Tutorid})" : session.Tutorid ?? "unknown-tutor";
                    var studentFolderName = !string.IsNullOrWhiteSpace(item.StudentName)
                        ? $"{item.StudentName} ({session.Studentid})" : session.Studentid ?? "unknown-student";
                    var folderId = await _drive.GetRecordingFolderAsync(tutorFolderName, studentFolderName, ct);

                    await using var driveStream = File.OpenRead(tempPath);
                    driveFileId = await _drive.UploadAsync(driveStream, fileName, "audio/mp4", folderId, ct);
                }

                // Job "prewarm" giả — chỉ để EnsureUploadedFileAsync (ClassSessionVideoAiService) tìm
                // thấy GeminiFileUri còn hạn khi có job tóm tắt/điền báo cáo thật cho session này.
                _context.ClassSessionAiJobs.Add(new ClassSessionAiJob
                {
                    JobId = Guid.NewGuid(),
                    Classsessionid = session.Classsessionid,
                    Jobtype = ClassSessionAiJobType.Prewarm,
                    Requestedbyuserid = "system",
                    Status = ClassSessionAiJobStatus.Completed,
                    Geminifileuri = uploaded.Uri,
                    Geminifilename = uploaded.Name,
                    Geminifileexpiresat = TimeZoneHelper.UtcNow.AddHours(47),
                    Createdat = TimeZoneHelper.UtcNow,
                    Completedat = TimeZoneHelper.UtcNow
                });

                session.Audiorecordingurl = $"https://drive.google.com/file/d/{driveFileId}/view";
                session.Audiorecordings3key = null; // đã xử lý xong (cả Gemini lẫn Drive)
                await _context.SaveChangesAsync(ct);

                try
                {
                    await s3.DeleteObjectAsync(_rec.StorageBucket, key, ct);
                }
                catch (Exception delEx)
                {
                    _logger.LogWarning(delEx, "Đã relay audio xong nhưng xóa file S3 {Key} thất bại (không nghiêm trọng)", key);
                }

                _logger.LogInformation(
                    "Relayed audio-only recording: session={Session} geminiFile={Name} driveFileId={FileId}",
                    session.Classsessionid, uploaded.Name, driveFileId);
            }
            catch (Amazon.S3.Model.NoSuchKeyException)
            {
                // Giống nhánh video: coi như thất bại vĩnh viễn, không thử lại — nhánh cũ (tải video +
                // ffmpeg) trong ClassSessionVideoAiService vẫn tự chạy được khi có job thật.
                session.Audiorecordings3key = null;
                await _context.SaveChangesAsync(ct);
                _logger.LogWarning(
                    "Audio-only recording session {Session} không tồn tại trên S3, bỏ qua — job AI sẽ tự tải video khi cần.",
                    session.Classsessionid);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Relay audio-only recording session {Session} thất bại, sẽ thử lại vòng sau.",
                    session.Classsessionid);
            }
            finally
            {
                if (tempPath != null)
                {
                    try { File.Delete(tempPath); }
                    catch (Exception cleanupEx) { _logger.LogWarning(cleanupEx, "Không xoá được file tạm {Path}.", tempPath); }
                }
            }
        }
    }

    private async Task NotifyRecordingReadyAsync(int classSessionId, string? studentId, string? tutorId, string? parentId)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(studentId))
            {
                await _notificationService.CreateNotificationAsync(new NotificationRequest
                {
                    Userid = studentId,
                    Title = "Video buổi học đã sẵn sàng",
                    Message = $"Video buổi học #{classSessionId} đã xem lại được. Vào chi tiết buổi học để xem hoặc dùng AI tóm tắt.",
                    Type = NotificationType.LessonRecordingReady,
                    Referenceid = classSessionId.ToString()
                });
            }
            if (!string.IsNullOrWhiteSpace(tutorId))
            {
                await _notificationService.CreateNotificationAsync(new NotificationRequest
                {
                    Userid = tutorId,
                    Title = "Video buổi học đã sẵn sàng",
                    Message = $"Video buổi học #{classSessionId} đã xem lại được. Vào chi tiết buổi học để xem hoặc dùng AI hỗ trợ điền báo cáo.",
                    Type = NotificationType.LessonRecordingReady,
                    Referenceid = classSessionId.ToString()
                });
            }
            // Chỉ có giá trị với hồ sơ học sinh do phụ huynh quản lý (Studentprofile.Parentid) — học
            // sinh tự đăng ký thì null, tự bỏ qua.
            if (!string.IsNullOrWhiteSpace(parentId))
            {
                await _notificationService.CreateNotificationAsync(new NotificationRequest
                {
                    Userid = parentId,
                    Title = "Video buổi học đã sẵn sàng",
                    Message = $"Video buổi học #{classSessionId} của con bạn đã xem lại được. Vào chi tiết buổi học để xem.",
                    Type = NotificationType.LessonRecordingReady,
                    Referenceid = classSessionId.ToString()
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Không gửi được thông báo video sẵn sàng cho classSession {ClassSessionId}", classSessionId);
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
