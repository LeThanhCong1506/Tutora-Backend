using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.Helpers;
using MV.ApplicationLayer.Interfaces;
using MV.ApplicationLayer.RepositoryInterfaces;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Entities;
using MV.DomainLayer.Exceptions;
using MV.DomainLayer.Helpers;
using System.Text.Json;

namespace MV.ApplicationLayer.Services;

public class ClassSessionVideoAiService(
    IAppDbContext db,
    IClassSessionService classSessionService,
    IGoogleDriveService driveService,
    IGeminiVideoAnalysisService geminiService,
    IAiChatRepository aiChatRepo,
    IStudentRepository studentRepo,
    INotificationService notificationService,
    IBackgroundJobClient backgroundJobClient,
    ILogger<ClassSessionVideoAiService> logger) : IClassSessionVideoAiService
{
    private const string VideoMimeType = "video/mp4";
    private const long MaxFileSizeBytes = 2_000_000_000;

    // ── Học sinh: tóm tắt ──────────────────────────────────────────────

    public async Task<ClassSessionAiJobResponse> TriggerStudentSummaryAsync(int classSessionId, string studentUserId, CancellationToken ct = default)
    {
        var profile = await studentRepo.FindByStudentOrLinkedUserAsync(studentUserId)
            ?? throw new StudentNotFoundException();

        var session = await LoadSessionForAuthAsync(classSessionId, ct)
            ?? throw new KeyNotFoundException("Không tìm thấy buổi học.");
        if (session.Studentid != profile.Studentid)
            throw new UnauthorizedAccessException("Bạn không có quyền truy cập buổi học này.");

        EnsureRecordingAvailable(session);

        var existing = await FindActiveJobAsync(classSessionId, ClassSessionAiJobType.StudentSummary, ct);
        if (existing != null)
            return ToResponse(existing);

        var job = await CreateJobAsync(classSessionId, ClassSessionAiJobType.StudentSummary, studentUserId, ct);
        backgroundJobClient.Enqueue<IClassSessionVideoAiService>(s => s.RunStudentSummaryJobAsync(job.JobId, true));
        return ToResponse(job);
    }

    public async Task<ClassSessionAiJobResponse> GetStudentSummaryStatusAsync(int classSessionId, string studentUserId, CancellationToken ct = default)
    {
        var profile = await studentRepo.FindByStudentOrLinkedUserAsync(studentUserId)
            ?? throw new StudentNotFoundException();
        var session = await LoadSessionForAuthAsync(classSessionId, ct)
            ?? throw new KeyNotFoundException("Không tìm thấy buổi học.");
        if (session.Studentid != profile.Studentid)
            throw new UnauthorizedAccessException("Bạn không có quyền truy cập buổi học này.");

        var job = await FindLatestJobAsync(classSessionId, ClassSessionAiJobType.StudentSummary, ct);
        return job == null ? new ClassSessionAiJobResponse { Status = "none" } : ToResponse(job);
    }

    public async Task<string> AskFollowUpAsync(int classSessionId, string studentUserId, string question, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(question))
            throw new InvalidOperationException("Câu hỏi không được để trống.");

        var profile = await studentRepo.FindByStudentOrLinkedUserAsync(studentUserId)
            ?? throw new StudentNotFoundException();
        var session = await LoadSessionForAuthAsync(classSessionId, ct)
            ?? throw new KeyNotFoundException("Không tìm thấy buổi học.");
        if (session.Studentid != profile.Studentid)
            throw new UnauthorizedAccessException("Bạn không có quyền truy cập buổi học này.");

        // Nguồn sự thật cho tóm tắt là job đã Completed (luôn được lưu thành công), không phải việc
        // phiên chat có được tạo đúng hay không — tránh phụ thuộc vào 1 bước phụ (seed chat) có thể lỗi.
        var summaryJob = await db.ClassSessionAiJobs.AsNoTracking()
            .Where(j => j.Classsessionid == classSessionId
                && j.Jobtype == ClassSessionAiJobType.StudentSummary
                && j.Status == ClassSessionAiJobStatus.Completed
                && j.Resulttext != null)
            .OrderByDescending(j => j.Completedat)
            .FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("Chưa có tóm tắt video cho buổi học này.");

        // Transcript chi tiết hơn tóm tắt — ưu tiên dùng làm ngữ cảnh nếu có (job cũ trước tính năng này thì chưa có).
        var context = summaryJob.Transcripttext ?? summaryJob.Resulttext!;
        var chatSession = await EnsureVideoSummaryChatSessionAsync(classSessionId, studentUserId, context);
        var (history, summary) = await LoadChatContextAsync(chatSession.SessionId);
        var summaryText = summary ?? context;

        var now = TimeZoneHelper.UtcNow;
        aiChatRepo.AddMessage(new ChatHistory
        {
            MessageId = Guid.NewGuid(),
            SessionId = chatSession.SessionId,
            Role = ChatHistoryRole.User,
            Content = question,
            CreatedAt = now
        });
        chatSession.UpdatedAt = now;
        aiChatRepo.UpdateSession(chatSession);
        await aiChatRepo.SaveChangesAsync();

        var answer = await geminiService.AskFollowUpAsync(summaryText, history, question, ct);

        aiChatRepo.AddMessage(new ChatHistory
        {
            MessageId = Guid.NewGuid(),
            SessionId = chatSession.SessionId,
            Role = ChatHistoryRole.Assistant,
            Content = answer,
            CreatedAt = TimeZoneHelper.UtcNow
        });
        await aiChatRepo.SaveChangesAsync();

        return answer;
    }

    public async Task<List<ClassSessionVideoChatMessageResponse>> GetFollowUpMessagesAsync(int classSessionId, string studentUserId, CancellationToken ct = default)
    {
        var profile = await studentRepo.FindByStudentOrLinkedUserAsync(studentUserId)
            ?? throw new StudentNotFoundException();
        var session = await LoadSessionForAuthAsync(classSessionId, ct)
            ?? throw new KeyNotFoundException("Không tìm thấy buổi học.");
        if (session.Studentid != profile.Studentid)
            throw new UnauthorizedAccessException("Bạn không có quyền truy cập buổi học này.");

        var chatSession = await aiChatRepo.FindSessionByUserAndClassSessionAsync(studentUserId, ChatSessionType.VideoSummary, classSessionId);
        if (chatSession == null) return new List<ClassSessionVideoChatMessageResponse>();

        var (items, _) = await aiChatRepo.GetMessagesPagedAsync(chatSession.SessionId, 1, 200);
        // Bỏ message "system" (tóm tắt gốc) khỏi khung chat — FE hiện tóm tắt riêng ở khối trên chat.
        return items
            .Where(m => m.Role != ChatHistoryRole.System)
            .Select(m => new ClassSessionVideoChatMessageResponse { Role = m.Role, Content = m.Content, CreatedAt = m.CreatedAt })
            .ToList();
    }

    // ── Gia sư: auto-fill báo cáo ──────────────────────────────────────

    public async Task<ClassSessionAiJobResponse> TriggerTutorReportFillAsync(int classSessionId, string tutorUserId, CancellationToken ct = default)
    {
        var session = await LoadSessionForAuthAsync(classSessionId, ct)
            ?? throw new KeyNotFoundException("Không tìm thấy buổi học.");
        if (session.Tutorid != tutorUserId)
            throw new UnauthorizedAccessException("Bạn không có quyền truy cập buổi học này.");

        EnsureRecordingAvailable(session);

        var existing = await FindActiveJobAsync(classSessionId, ClassSessionAiJobType.TutorReportFill, ct);
        if (existing != null)
            return ToResponse(existing);

        var job = await CreateJobAsync(classSessionId, ClassSessionAiJobType.TutorReportFill, tutorUserId, ct);
        backgroundJobClient.Enqueue<IClassSessionVideoAiService>(s => s.RunTutorReportFillJobAsync(job.JobId, true));
        return ToResponse(job);
    }

    public async Task<ClassSessionAiJobResponse> GetTutorReportFillStatusAsync(int classSessionId, string tutorUserId, CancellationToken ct = default)
    {
        var session = await LoadSessionForAuthAsync(classSessionId, ct)
            ?? throw new KeyNotFoundException("Không tìm thấy buổi học.");
        if (session.Tutorid != tutorUserId)
            throw new UnauthorizedAccessException("Bạn không có quyền truy cập buổi học này.");

        var job = await FindLatestJobAsync(classSessionId, ClassSessionAiJobType.TutorReportFill, ct);
        return job == null ? new ClassSessionAiJobResponse { Status = "none" } : ToResponse(job);
    }

    // ── Hangfire job bodies ────────────────────────────────────────────

    public async Task RunStudentSummaryJobAsync(Guid jobId, bool swallowFailure)
    {
        var job = await db.ClassSessionAiJobs.FirstOrDefaultAsync(j => j.JobId == jobId);
        if (job == null)
        {
            logger.LogWarning("RunStudentSummaryJobAsync: job {JobId} not found", jobId);
            return;
        }

        try
        {
            job.Status = ClassSessionAiJobStatus.Processing;
            job.Stage = ClassSessionAiJobStage.Analyzing;
            await db.SaveChangesAsync();

            var file = await EnsureUploadedFileAsync(job, CancellationToken.None);
            // Bỏ lượt xem lại video lần 2 để tự soát (VerifyStudentAnalysisAsync) — tốn gần gấp đôi
            // thời gian chờ cho một bước cải thiện nhỏ, đổi lấy UX nhanh hơn rõ rệt.
            var analysis = await geminiService.AnalyzeVideoForStudentAsync(file.Uri, VideoMimeType, CancellationToken.None);

            job.Resulttext = analysis.Summary;
            job.Transcripttext = analysis.Transcript;
            job.Status = ClassSessionAiJobStatus.Completed;
            job.Stage = null;
            job.Completedat = TimeZoneHelper.UtcNow;
            await db.SaveChangesAsync();

            // Tách try/catch riêng: lỗi ở bước này (tạo phiên chat) không được làm job quay lại
            // Failed — tóm tắt đã lưu xong là thành công rồi, chat hỏi tiếp chỉ là phần thêm.
            try
            {
                logger.LogInformation(
                    "[VideoSummaryChat] Bắt đầu tạo phiên chat cho classSession {ClassSessionId}, user {UserId}",
                    job.Classsessionid, job.Requestedbyuserid);
                // Transcript chi tiết hơn tóm tắt — dùng làm ngữ cảnh chat hỏi tiếp cho câu trả lời chính xác hơn.
                await EnsureVideoSummaryChatSessionAsync(job.Classsessionid, job.Requestedbyuserid, analysis.Transcript);
                logger.LogInformation(
                    "[VideoSummaryChat] Đã tạo xong phiên chat cho classSession {ClassSessionId}",
                    job.Classsessionid);
            }
            catch (Exception seedEx)
            {
                logger.LogError(seedEx,
                    "[VideoSummaryChat] LỖI khi tạo phiên chat cho classSession {ClassSessionId}, user {UserId}",
                    job.Classsessionid, job.Requestedbyuserid);
            }

            await NotifyAsync(
                job.Requestedbyuserid,
                "Tóm tắt buổi học đã sẵn sàng",
                $"Video buổi học #{job.Classsessionid} đã được tóm tắt xong. Vào chi tiết buổi học để xem.",
                NotificationType.LessonVideoSummaryReady,
                job.Classsessionid);
        }
        catch (Exception ex) when (swallowFailure)
        {
            await MarkJobFailedAsync(job, ex);
        }
    }

    public async Task RunTutorReportFillJobAsync(Guid jobId, bool swallowFailure)
    {
        var job = await db.ClassSessionAiJobs.FirstOrDefaultAsync(j => j.JobId == jobId);
        if (job == null)
        {
            logger.LogWarning("RunTutorReportFillJobAsync: job {JobId} not found", jobId);
            return;
        }

        try
        {
            job.Status = ClassSessionAiJobStatus.Processing;
            await db.SaveChangesAsync();

            var file = await EnsureUploadedFileAsync(job, CancellationToken.None);
            var result = await geminiService.GenerateTutorReportFieldsAsync(file.Uri, VideoMimeType, CancellationToken.None);

            job.Resultjson = JsonSerializer.Serialize(result);
            job.Status = ClassSessionAiJobStatus.Completed;
            job.Completedat = TimeZoneHelper.UtcNow;
            await db.SaveChangesAsync();

            await NotifyAsync(
                job.Requestedbyuserid,
                "Gợi ý báo cáo buổi học đã sẵn sàng",
                $"AI đã điền sẵn nội dung báo cáo cho buổi học #{job.Classsessionid}. Vào trang báo cáo để xem/sửa trước khi nộp.",
                NotificationType.LessonReportAiFillReady,
                job.Classsessionid);
        }
        catch (Exception ex) when (swallowFailure)
        {
            await MarkJobFailedAsync(job, ex);
        }
    }

    // ── Helpers dùng chung ─────────────────────────────────────────────

    /// <summary>Tái dùng GeminiFileUri còn hạn của bất kỳ job nào (2 loại) thuộc cùng buổi học; nếu không có thì tải từ Drive + upload mới.</summary>
    private async Task<GeminiUploadedFile> EnsureUploadedFileAsync(ClassSessionAiJob job, CancellationToken ct)
    {
        var now = TimeZoneHelper.UtcNow;
        var cached = await db.ClassSessionAiJobs
            .Where(j => j.Classsessionid == job.Classsessionid
                && j.Geminifileuri != null && j.Geminifilename != null
                && j.Geminifileexpiresat != null && j.Geminifileexpiresat > now)
            .OrderByDescending(j => j.Createdat)
            .FirstOrDefaultAsync(ct);

        if (cached != null)
            return new GeminiUploadedFile(cached.Geminifilename!, cached.Geminifileuri!);

        var fileId = await classSessionService.GetRecordingDriveFileIdAsync(job.Classsessionid)
            ?? throw new GeminiFileProcessingException("Buổi học chưa có video để phân tích.");

        GeminiUploadedFile uploaded;
        using (var media = await driveService.GetMediaAsync(fileId, null, ct))
        {
            if (media.ContentLength is { } size && size > MaxFileSizeBytes)
                throw new GeminiVideoTooLargeException();

            uploaded = await geminiService.UploadVideoAsync(
                media.Content, media.ContentLength ?? 0, VideoMimeType, $"class-session-{job.Classsessionid}.mp4", ct);
        }
        await geminiService.WaitForFileActiveAsync(uploaded.Name, ct);

        job.Geminifileuri = uploaded.Uri;
        job.Geminifilename = uploaded.Name;
        // Gemini giữ file ~48h — trừ hao 1h để không dùng cache sát nút hết hạn.
        job.Geminifileexpiresat = TimeZoneHelper.UtcNow.AddHours(47);
        await db.SaveChangesAsync(ct);

        return uploaded;
    }

    private async Task MarkJobFailedAsync(ClassSessionAiJob job, Exception ex)
    {
        logger.LogError(ex, "Job {JobId} (type={JobType}, classSession={ClassSessionId}) thất bại.",
            job.JobId, job.Jobtype, job.Classsessionid);
        job.Status = ClassSessionAiJobStatus.Failed;
        job.Errormessage = ex is BadRequestException ? ex.Message : "Có lỗi xảy ra, vui lòng thử lại.";
        job.Completedat = TimeZoneHelper.UtcNow;
        try
        {
            await db.SaveChangesAsync();
        }
        catch (Exception saveEx)
        {
            logger.LogError(saveEx, "Không lưu được trạng thái Failed cho job {JobId}.", job.JobId);
        }
    }

    private Task<ClassSessionAiJob?> FindActiveJobAsync(int classSessionId, string jobType, CancellationToken ct)
        => db.ClassSessionAiJobs
            .Where(j => j.Classsessionid == classSessionId && j.Jobtype == jobType
                && (j.Status == ClassSessionAiJobStatus.Pending || j.Status == ClassSessionAiJobStatus.Processing))
            .OrderByDescending(j => j.Createdat)
            .FirstOrDefaultAsync(ct);

    private Task<ClassSessionAiJob?> FindLatestJobAsync(int classSessionId, string jobType, CancellationToken ct)
        => db.ClassSessionAiJobs
            .AsNoTracking()
            .Where(j => j.Classsessionid == classSessionId && j.Jobtype == jobType)
            .OrderByDescending(j => j.Createdat)
            .FirstOrDefaultAsync(ct);

    private async Task<ClassSessionAiJob> CreateJobAsync(int classSessionId, string jobType, string userId, CancellationToken ct)
    {
        var job = new ClassSessionAiJob
        {
            JobId = Guid.NewGuid(),
            Classsessionid = classSessionId,
            Jobtype = jobType,
            Requestedbyuserid = userId,
            Status = ClassSessionAiJobStatus.Pending,
            Createdat = TimeZoneHelper.UtcNow
        };
        db.ClassSessionAiJobs.Add(job);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Race: 2 request cùng lúc đều pass qua FindActiveJobAsync trước khi cái đầu insert xong —
            // unique index là nguồn sự thật, request thua cuộc dùng lại job của người thắng.
            db.ClassSessionAiJobs.Remove(job);
            var winner = await FindActiveJobAsync(classSessionId, jobType, ct)
                ?? throw new InvalidOperationException("Không thể tạo yêu cầu phân tích video.");
            return winner;
        }
        return job;
    }

    private Task<ClassSession?> LoadSessionForAuthAsync(int classSessionId, CancellationToken ct)
        => db.ClassSessions.AsNoTracking().FirstOrDefaultAsync(s => s.Classsessionid == classSessionId, ct);

    private static void EnsureRecordingAvailable(ClassSession session)
    {
        var (status, _) = RecordingStatusResolver.Resolve(
            session.Recordingurl, session.Recordings3key, session.Recordingsid, session.Checkouttime.HasValue);
        if (status != "available")
            throw new InvalidOperationException("Video buổi học chưa sẵn sàng để phân tích.");
    }

    private static ClassSessionAiJobResponse ToResponse(ClassSessionAiJob job) => new()
    {
        JobId = job.JobId,
        Status = job.Status,
        Stage = job.Stage,
        ResultText = job.Resulttext,
        TranscriptText = job.Transcripttext,
        ResultJson = job.Resultjson != null ? JsonSerializer.Deserialize<TutorReportAiFillResult>(job.Resultjson) : null,
        ErrorMessage = job.Errormessage
    };

    private async Task NotifyAsync(string userId, string title, string message, string type, int classSessionId)
    {
        try
        {
            await notificationService.CreateNotificationAsync(new NotificationRequest
            {
                Userid = userId,
                Title = title,
                Message = message,
                Type = type,
                Referenceid = classSessionId.ToString()
            });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Không gửi được thông báo {Type} cho user {UserId}", type, userId);
        }
    }

    /// <summary>Tìm hoặc tạo phiên chat tóm tắt cho (user, classSessionId); tự vá message "system" còn thiếu
    /// nếu phiên đã tồn tại nhưng vì lý do gì đó chưa có tóm tắt (self-healing, không phụ thuộc bước seed
    /// trong Hangfire job phải chạy đúng thì chat mới dùng được).</summary>
    private async Task<ChatSession> EnsureVideoSummaryChatSessionAsync(int classSessionId, string userId, string summary)
    {
        var existing = await aiChatRepo.FindSessionByUserAndClassSessionAsync(userId, ChatSessionType.VideoSummary, classSessionId);
        var now = TimeZoneHelper.UtcNow;
        bool needsSystemMessage;

        if (existing == null)
        {
            existing = new ChatSession
            {
                SessionId = Guid.NewGuid(),
                UserId = userId,
                SessionType = ChatSessionType.VideoSummary,
                ClassSessionId = classSessionId,
                Title = $"Tóm tắt buổi học #{classSessionId}",
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now
            };
            aiChatRepo.AddSession(existing);
            needsSystemMessage = true;
        }
        else
        {
            // Message system (tóm tắt gốc) luôn được chèn đầu tiên nên nếu có sẽ nằm ở trang đầu tiên.
            var (items, _) = await aiChatRepo.GetMessagesPagedAsync(existing.SessionId, 1, 1);
            needsSystemMessage = items.Count == 0 || items[0].Role != ChatHistoryRole.System;
        }

        if (needsSystemMessage)
        {
            aiChatRepo.AddMessage(new ChatHistory
            {
                MessageId = Guid.NewGuid(),
                SessionId = existing.SessionId,
                Role = ChatHistoryRole.System,
                Content = summary,
                CreatedAt = now
            });
            existing.UpdatedAt = now;
            await aiChatRepo.SaveChangesAsync();
        }

        return existing;
    }

    /// <summary>Trả (lịch sử user/assistant để đưa Gemini, tóm tắt gốc lấy từ message system mới nhất).</summary>
    private async Task<(IReadOnlyList<GeminiChatTurn> History, string? Summary)> LoadChatContextAsync(Guid sessionId)
    {
        var (items, _) = await aiChatRepo.GetMessagesPagedAsync(sessionId, 1, 200);
        var summary = items.LastOrDefault(m => m.Role == ChatHistoryRole.System)?.Content;
        var history = items
            .Where(m => m.Role != ChatHistoryRole.System)
            .Select(m => new GeminiChatTurn(m.Role, m.Content))
            .ToList();
        return (history, summary);
    }
}
