using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
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

    private readonly IAppDbContext _context;
    private readonly IGoogleDriveService _drive;
    private readonly INotificationService _notificationService;
    private readonly IBackgroundJobClient _backgroundJobClient;
    private readonly IGeminiVideoAnalysisService _gemini;
    private readonly AgoraRecordingSettings _rec;
    private readonly ILogger<RecordingRelayService> _logger;

    public RecordingRelayService(
        IAppDbContext context,
        IGoogleDriveService drive,
        INotificationService notificationService,
        IBackgroundJobClient backgroundJobClient,
        IGeminiVideoAnalysisService gemini,
        IOptions<AgoraRecordingSettings> rec,
        ILogger<RecordingRelayService> logger)
    {
        _context = context;
        _drive = drive;
        _notificationService = notificationService;
        _backgroundJobClient = backgroundJobClient;
        _gemini = gemini;
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
    private async Task RelayPendingAudioAsync(CancellationToken ct)
    {
        var pending = await _context.ClassSessions
            .Where(s => s.Audiorecordings3key != null)
            .OrderBy(s => s.Classsessionid)
            .Take(BatchSize)
            .ToListAsync(ct);

        if (pending.Count == 0) return;

        using var s3 = CreateS3Client();

        foreach (var session in pending)
        {
            var key = session.Audiorecordings3key!;
            try
            {
                long contentLength;
                MV.DomainLayer.DTO.ResponseModel.GeminiUploadedFile uploaded;
                using (var s3Obj = await s3.GetObjectAsync(_rec.StorageBucket, key, ct))
                {
                    contentLength = s3Obj.ContentLength;
                    uploaded = await _gemini.UploadVideoAsync(
                        s3Obj.ResponseStream, contentLength, "audio/mp4",
                        $"class-session-{session.Classsessionid}-audio.mp4", ct);
                }
                await _gemini.WaitForFileActiveAsync(uploaded.Name, ct);

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

                session.Audiorecordings3key = null; // đã xử lý xong
                await _context.SaveChangesAsync(ct);

                try
                {
                    await s3.DeleteObjectAsync(_rec.StorageBucket, key, ct);
                }
                catch (Exception delEx)
                {
                    _logger.LogWarning(delEx, "Đã forward audio lên Gemini nhưng xóa file S3 {Key} thất bại (không nghiêm trọng)", key);
                }

                _logger.LogInformation(
                    "Forwarded audio-only recording lên Gemini: session={Session} geminiFile={Name}",
                    session.Classsessionid, uploaded.Name);
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
                    "Forward audio-only recording session {Session} lên Gemini thất bại, sẽ thử lại vòng sau.",
                    session.Classsessionid);
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
