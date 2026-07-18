using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Entities;
using MV.DomainLayer.Exceptions;
using MV.DomainLayer.Helpers;
using System.Text.Json;
using static MV.DomainLayer.Constants.ClassSessionStatus;

namespace MV.ApplicationLayer.Services;

public partial class ClassSessionService
{
    // ── M3-T2: Check-in / Check-out / Report ─────────────────────────────────

    public async Task<ClassSessionDetailResponse> CheckInAsync(int classSessionId, string tutorId, CheckInRequest request)
    {
        var classSession = await _context.ClassSessions
            .Include(l => l.Booking)
            .FirstOrDefaultAsync(l => l.Classsessionid == classSessionId && l.Tutorid == tutorId)
            ?? throw new ClassSessionException(ClassSessionErrorCodes.ClassSessionNotFound, "Không tìm thấy buổi học", 404);

        if (classSession.Status != Scheduled)
            throw new ClassSessionException(ClassSessionErrorCodes.InvalidClassSessionStatus, "Buổi học không ở trạng thái đã lên lịch", 400);

        // Block check-in for subsequent classSessions if remaining 50% not paid yet
        if (await IsNextSessionBlockedByRemainingPaymentAsync(classSession.Booking, classSession.Bookingid, classSessionId))
            throw new ClassSessionException(BookingErrorCodes.RemainingNotPaid,
                "Phụ huynh chưa thanh toán các buổi học còn lại. Vui lòng đợi thanh toán trước khi bắt đầu buổi tiếp theo.", 400);

        var now = TimeZoneHelper.UtcNow;
        var minutesDiff = Math.Abs((now - classSession.Scheduledstart).TotalMinutes);
        if (minutesDiff > 15)
            throw new ClassSessionException(ClassSessionErrorCodes.CheckInTooEarly, "Chỉ được điểm danh trong vòng ±15 phút so với giờ bắt đầu", 400);

        classSession.Checkintime = now;
        classSession.Realstart = now;
        classSession.Status = InProgress;
        classSession.Istutorpresent = true;

        await _context.SaveChangesAsync();
        _logger.LogInformation("Tutor {TutorId} checked in to classSession {ClassSessionId}", tutorId, classSessionId);

        // ── Tạo Meet link nếu chưa có (online/hybrid) ──
        var teachingMode = TeachingMode.Online;
        var isOnlineMode = true;

        _logger.LogInformation("CheckIn classSession {ClassSessionId}: teachingMode={Mode}, isOnlineMode={IsOnline}, hasMeetLink={HasLink}, studentId={StudentId}",
            classSessionId, teachingMode, isOnlineMode, !string.IsNullOrWhiteSpace(classSession.Meetinglink), classSession.Booking?.Studentid);

        if (isOnlineMode && string.IsNullOrWhiteSpace(classSession.Meetinglink) && classSession.Booking?.Studentid != null)
        {
            // Agora RTC: channel = classSessionId (không cần OAuth, không cần external API)
            classSession.Meetinglink = classSessionId.ToString();
            _logger.LogInformation("Assigned Agora RTC channel {Channel} for classSession {ClassSessionId}", classSessionId, classSessionId);
            await _context.SaveChangesAsync();
        }

        // Ghi hình KHÔNG còn bắt đầu ở check-in nữa — chuyển sang lúc Tutor "Vào lớp" (join call),
        // gọi qua StartSessionRecordingAsync. Xem AgoraController.StartRecording.

        // ── Gửi thông báo + link vào chat cho Parent và Student ──
        var parentId = classSession.Booking?.Parentid;
        var studentProfileId = classSession.Booking?.Studentid; // ProfileId (stu_xxx), KHÔNG phải UserId
        var classSessionTimeVn = classSession.Scheduledstart.ToString("dd/MM HH:mm");
        // hasMeetLink được tính lại SAU khi Agora RTC channel đã được set
        var hasMeetLink = !string.IsNullOrWhiteSpace(classSession.Meetinglink);
        string chatContent;
        string messageType;
        if (hasMeetLink)
        {
            chatContent = $"🟢 Buổi học đã bắt đầu lúc {classSessionTimeVn}!\n\n🔗 Tham gia lớp học trực tuyến (Mã phòng: {classSession.Meetinglink}):\nĐể vào phòng học, vui lòng mở ứng dụng và nhập Mã phòng: {classSession.Meetinglink}";
            messageType = ChatMessageType.MeetLink;
        }
        else
        {
            chatContent = $"🟢 Buổi học đã bắt đầu lúc {classSessionTimeVn}.";
            messageType = ChatMessageType.Text;
        }


        // Resolve Student LinkedUserId (UserId thực sự để tạo channel/notification)
        string? studentLinkedUserId = null;
        if (!string.IsNullOrEmpty(studentProfileId))
        {
            studentLinkedUserId = await _context.Studentprofiles
                .Where(s => s.Studentid == studentProfileId)
                .Select(s => s.Linkeduserid)
                .FirstOrDefaultAsync();

            _logger.LogInformation("CheckIn classSession {ClassSessionId}: studentProfileId={ProfileId}, studentLinkedUserId={LinkedId}",
                classSessionId, studentProfileId, studentLinkedUserId ?? "null");
        }

        // Chuẩn bị metadata chat
        var chatMetadata = new
        {
            classSessionId,
            meetingLink = classSession.Meetinglink,
            scheduledStart = classSession.Scheduledstart
        };

        // ── Gửi cho Parent ──
        if (!string.IsNullOrEmpty(parentId))
        {
            try
            {
                var parentChannelId = await _chatService.GetOrCreateChannelAsync(parentId, tutorId);
                await _chatService.SendMessageAsync(tutorId, parentChannelId, new ChatMessageCreateRequest
                {
                    Content     = chatContent,
                    MessageType = messageType,
                    Metadata    = chatMetadata
                });
                _logger.LogInformation("Sent check-in chat to parent channel {ChannelId} for classSession {ClassSessionId}", parentChannelId, classSessionId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send check-in chat to parent for classSession {ClassSessionId}", classSessionId);
            }

            // Push notification cho Parent
            try
            {
                await _notificationService.CreateNotificationAsync(new NotificationRequest
                {
                    Userid = parentId,
                    Title = "Buổi học đã bắt đầu",
                    Message = "Gia sư đã bắt đầu buổi học." +
                                  (hasMeetLink ? " Kiểm tra tin nhắn để lấy Mã phòng tham gia lớp học trực tuyến." : ""),
                    Type = NotificationType.LessonCheckin,
                    Referenceid = classSessionId.ToString()
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send check-in notification to parent for classSession {ClassSessionId}", classSessionId);
            }
        }

        // ── Gửi cho Student (chỉ khi student có tài khoản riêng - linkedUserId != null) ──
        if (!string.IsNullOrEmpty(studentLinkedUserId))
        {
            try
            {
                // Dùng studentLinkedUserId (UserId thực) để tạo/tìm channel student-tutor
                var studentChannelId = await _chatService.GetOrCreateChannelAsync(studentLinkedUserId, tutorId, isStudent: true);
                await _chatService.SendMessageAsync(tutorId, studentChannelId, new ChatMessageCreateRequest
                {
                    Content     = chatContent,
                    MessageType = messageType,
                    Metadata    = chatMetadata
                });
                _logger.LogInformation("Sent check-in chat to student channel {ChannelId} for classSession {ClassSessionId}", studentChannelId, classSessionId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send check-in chat to student for classSession {ClassSessionId}", classSessionId);
            }

            // Push notification cho Student (dùng linkedUserId)
            try
            {
                await _notificationService.CreateNotificationAsync(new NotificationRequest
                {
                    Userid = studentLinkedUserId,
                    Title = "Buổi học đã bắt đầu",
                    Message = "Gia sư đã bắt đầu buổi học." +
                                  (hasMeetLink ? " Kiểm tra tin nhắn để lấy Mã phòng tham gia lớp học trực tuyến." : ""),
                    Type = NotificationType.LessonCheckin,
                    Referenceid = classSessionId.ToString()
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send check-in notification to student for classSession {ClassSessionId}", classSessionId);
            }
        }

        return (await GetTutorClassSessionDetailAsync(classSessionId, tutorId))!;
    }

    public async Task<ClassSessionDetailResponse> CheckOutAsync(int classSessionId, string tutorId, CheckOutRequest request)
    {
        var classSession = await _context.ClassSessions
            .Include(l => l.Booking)
            .FirstOrDefaultAsync(l => l.Classsessionid == classSessionId && l.Tutorid == tutorId)
            ?? throw new ClassSessionException(ClassSessionErrorCodes.ClassSessionNotFound, "Không tìm thấy buổi học", 404);

        if (classSession.Status != InProgress)
            throw new ClassSessionException(ClassSessionErrorCodes.InvalidClassSessionStatus, "Buổi học không ở trạng thái đang diễn ra", 400);

        if (!classSession.Checkintime.HasValue)
            throw new ClassSessionException(ClassSessionErrorCodes.NotCheckedIn, "Vui lòng điểm danh vào trước", 400);

        classSession.Checkouttime = TimeZoneHelper.UtcNow;
        classSession.Realend = TimeZoneHelper.UtcNow;

        await _context.SaveChangesAsync();
        _logger.LogInformation("Tutor {TutorId} checked out from classSession {ClassSessionId}", tutorId, classSessionId);

        // ── Tự động dừng Cloud Recording (nếu đang record) ──
        await TryStopRecordingAsync(classSession);

        return (await GetTutorClassSessionDetailAsync(classSessionId, tutorId))!;
    }

    /// <summary>
    /// Bắt đầu ghi hình buổi học — gọi khi TUTOR vào lớp (join call), thay cho lúc check-in.
    /// Query gắn Tutorid nên chỉ tutor của buổi mới gọi được (sai tutor → không tìm thấy).
    /// </summary>
    public async Task StartSessionRecordingAsync(int classSessionId, string tutorId)
    {
        var classSession = await _context.ClassSessions
            .FirstOrDefaultAsync(l => l.Classsessionid == classSessionId && l.Tutorid == tutorId)
            ?? throw new ClassSessionException(ClassSessionErrorCodes.ClassSessionNotFound, "Không tìm thấy buổi học", 404);

        await TryStartRecordingAsync(classSession);
    }

    /// <summary>
    /// Bắt đầu Agora Cloud Recording cho buổi học (nếu tính năng bật).
    /// Lỗi record KHÔNG được làm hỏng luồng gọi → nuốt exception, chỉ log cảnh báo.
    /// </summary>
    private async Task TryStartRecordingAsync(ClassSession classSession)
    {
        if (!_cloudRecording.Enabled) return;
        if (!string.IsNullOrEmpty(classSession.Recordingsid)) return; // đã đang record

        try
        {
            var handle = await _cloudRecording.StartAsync(classSession.Classsessionid);
            classSession.Recordingresourceid = handle.ResourceId;
            classSession.Recordingsid = handle.Sid;
            await _context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Không thể bắt đầu Cloud Recording cho buổi học {ClassSessionId}", classSession.Classsessionid);
        }
    }

    /// <summary>
    /// Dừng Cloud Recording và lưu link. Lỗi KHÔNG làm hỏng check-out → nuốt exception, chỉ log.
    /// </summary>
    private async Task TryStopRecordingAsync(ClassSession classSession)
    {
        if (!_cloudRecording.Enabled) return;
        if (string.IsNullOrEmpty(classSession.Recordingresourceid) || string.IsNullOrEmpty(classSession.Recordingsid))
            return;

        try
        {
            var result = await _cloudRecording.StopAsync(
                classSession.Classsessionid, classSession.Recordingresourceid, classSession.Recordingsid);

            // Lấy S3 object key của file .mp4 để job relay đẩy lên Google Drive
            string? mp4Key = null;
            foreach (var f in result.FileNames)
            {
                if (f.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)) { mp4Key = f; break; }
            }
            if (mp4Key == null && result.FileNames.Count > 0) mp4Key = result.FileNames[0];

            classSession.Recordings3key = mp4Key;            // job relay sẽ đẩy lên Drive rồi xóa file S3
            classSession.Recordingurl = result.PlaybackUrl;  // link S3 tạm (nếu có PublicUrlBase)
            await _context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Không thể dừng Cloud Recording cho buổi học {ClassSessionId}", classSession.Classsessionid);
        }
    }

    public async Task<ClassSessionDetailResponse> SubmitReportAsync(int classSessionId, string tutorId, SubmitReportRequest request)
    {
        await using var tx = await _context.Database.BeginTransactionAsync();
        try
        {
            var classSession = await _context.ClassSessions
                .Include(l => l.Booking)
                    .ThenInclude(b => b!.Student)
                .Include(l => l.ClassSessionReport)
                .FirstOrDefaultAsync(l => l.Classsessionid == classSessionId && l.Tutorid == tutorId)
                ?? throw new ClassSessionException(ClassSessionErrorCodes.ClassSessionNotFound, "Không tìm thấy buổi học", 404);

            // MVP Phase 2: Bỏ check-in/check-out bắt buộc

            if (classSession.Status != Scheduled && classSession.Status != InProgress)
                throw new ClassSessionException(ClassSessionErrorCodes.InvalidClassSessionStatus, "Buổi học phải ở trạng thái đã lên lịch hoặc đang diễn ra mới gửi được báo cáo", 400);

            if (classSession.ClassSessionReport != null)
                throw new ClassSessionException(ClassSessionErrorCodes.ReportAlreadySubmitted, "Báo cáo buổi học đã được gửi rồi", 400);

            var now = TimeZoneHelper.UtcNow;

            if (classSession.Status == Scheduled)
            {
                classSession.Isearlysubmission = true;
            }

            classSession.Lessoncontent = request.ContentCovered;
            classSession.Homework = request.HomeworkAssigned;
            classSession.Tutornotes = request.TutorNotes;
            classSession.Istutorpresent = request.IsTutorPresent;
            classSession.Isstudentpresent = request.IsStudentPresent;
            classSession.Attendancenote = request.AttendanceNote;
            classSession.Status = PendingConfirmation;
            classSession.Submittedat = now;
            classSession.Confirmdeadline = now.AddHours(24);

            var report = new ClassSessionReport
            {
                Classsessionid = classSessionId,
                Createdbytutorid = tutorId,
                Contentcovered = request.ContentCovered,
                Homeworkassigned = request.HomeworkAssigned,
                Studentperformancerating = request.StudentPerformanceRating,
                Attachments = request.Attachments != null ? JsonSerializer.Serialize(request.Attachments) : null,
                Createdat = now
            };
            _context.ClassSessionReports.Add(report);

            // Phát hiện buổi đầu tiên của booking deposit_paid TRƯỚC khi save,
            // để cập nhật booking status trong cùng transaction.
            var parentId = classSession.Booking?.Student?.Parentid;
            var isFirstClassSessionReport = classSession.Booking != null
                && classSession.Booking.Status == BookingStatus.DepositPaid
                && classSession.Booking.Remainingpaidat == null
                && !await _context.ClassSessions.AnyAsync(
                    l => l.Bookingid == classSession.Bookingid && l.Classsessionid != classSessionId
                    && (l.Status == PendingConfirmation || l.Status == Completed));

            if (isFirstClassSessionReport)
            {
                classSession.Booking!.Status = BookingStatus.PendingRemainingPayment;
                classSession.Booking.Paymentdueat = now.AddHours(48);
            }

            await _context.SaveChangesAsync();
            await tx.CommitAsync();

            _logger.LogInformation("Tutor {TutorId} submitted report for classSession {ClassSessionId}", tutorId, classSessionId);

            // Notify Parent — báo cáo buổi học
            if (!string.IsNullOrEmpty(parentId))
            {
                await _notificationService.CreateNotificationAsync(new NotificationRequest
                {
                    Userid = parentId,
                    Title = "Báo cáo buổi học mới",
                    Message = $"Gia sư đã gửi báo cáo cho buổi học #{classSessionId}. Vui lòng kiểm tra và xác nhận trong vòng 24h.",
                    Type = NotificationType.LessonReport,
                    Referenceid = classSessionId.ToString()
                });
            }

            // Notify Parent — yêu cầu thanh toán remaining trong 48h
            if (isFirstClassSessionReport && !string.IsNullOrEmpty(parentId))
            {
                await _notificationService.CreateNotificationAsync(new NotificationRequest
                {
                    Userid = parentId,
                    Title = "Cần thanh toán các buổi học còn lại",
                    Message = $"Buổi học đầu tiên của booking #{classSession.Bookingid} đã hoàn thành. " +
                        $"Bạn có 48h để thanh toán {classSession.Booking!.Remainingamount:N0}đ cho các buổi còn lại. " +
                        $"Nếu không thanh toán đúng hạn, booking sẽ bị hủy tự động.",
                    Type = NotificationType.PaymentRemainingRequired,
                    Referenceid = classSession.Bookingid.ToString()
                });
            }

            return (await GetTutorClassSessionDetailAsync(classSessionId, tutorId))!;
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    public async Task<string> UploadAttachmentAsync(int classSessionId, string tutorId, IFormFile file)
    {
        var classSession = await _context.ClassSessions
            .FirstOrDefaultAsync(l => l.Classsessionid == classSessionId && l.Tutorid == tutorId)
            ?? throw new ClassSessionException(ClassSessionErrorCodes.ClassSessionNotFound, "Không tìm thấy buổi học", 404);

        // Ensure bucket exists
        await _storageService.EnsureBucketExistsAsync(ClassSessionAttachmentBucket);

        // Upload to Storage using class-session-specific folder
        var folderPath = $"class-session-{classSessionId}";
        var fileUrl = await _storageService.UploadFileAsync(ClassSessionAttachmentBucket, folderPath, file);

        _logger.LogInformation("Uploaded attachment {FileName} for classSession {ClassSessionId} → {Url}", file.FileName, classSessionId, fileUrl);
        return fileUrl;
    }

    /// <summary>
    /// True nếu buổi học <paramref name="currentSessionId"/> là buổi TIẾP THEO của booking
    /// nhưng phụ huynh CHƯA thanh toán đợt 2. Điều kiện: booking đang ở deposit_paid /
    /// pending_remaining_payment, chưa có Remainingpaidat, và đã có ít nhất 1 buổi khác của
    /// booking đã bắt đầu/hoàn tất (completed / pending_confirmation / in_progress).
    /// Buổi ĐẦU luôn cho phép (chưa có buổi nào trước đó). Dùng chung cho check-in và cấp
    /// token Agora để chặn học tiếp khi chưa trả tiền các buổi còn lại.
    /// </summary>
    private async Task<bool> IsNextSessionBlockedByRemainingPaymentAsync(
        Booking? booking, int? bookingId, int currentSessionId)
    {
        if (booking == null || bookingId == null)
            return false;

        if (booking.Status != BookingStatus.DepositPaid && booking.Status != BookingStatus.PendingRemainingPayment)
            return false;

        if (booking.Remainingpaidat != null)
            return false;

        return await _context.ClassSessions.AnyAsync(
            l => l.Bookingid == bookingId && l.Classsessionid != currentSessionId
            && (l.Status == Completed || l.Status == PendingConfirmation || l.Status == InProgress));
    }

    /// <inheritdoc />
    public async Task<bool> IsSessionBlockedByRemainingPaymentAsync(int classSessionId)
    {
        var classSession = await _context.ClassSessions
            .Include(l => l.Booking)
            .FirstOrDefaultAsync(l => l.Classsessionid == classSessionId);

        if (classSession == null)
            return false;

        return await IsNextSessionBlockedByRemainingPaymentAsync(
            classSession.Booking, classSession.Bookingid, classSessionId);
    }
}
