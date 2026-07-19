using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.Services.Agora;
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

    /// <summary>
    /// Presence-driven auto check-in. Được gọi mỗi nhịp heartbeat: nếu cả gia sư và học viên
    /// (hoặc phụ huynh thay thế khi student chưa có tài khoản) cùng có mặt trong phòng của buổi
    /// đang ở <c>scheduled</c>, tự chuyển sang <c>in_progress</c> và ghi check-in. Một người
    /// có mặt không đủ. Idempotent — cập nhật có điều kiện, chỉ đổi trạng thái đúng một lần.
    /// </summary>
    public async Task<SessionPresenceStatus> TryAutoCheckInAsync(int classSessionId)
    {
        var classSession = await _context.ClassSessions
            .Include(l => l.Booking)
                .ThenInclude(b => b!.Student)
            .FirstOrDefaultAsync(l => l.Classsessionid == classSessionId);

        if (classSession == null)
            return new SessionPresenceStatus(false, false, false, false, false);

        var tutorId = classSession.Tutorid;
        var studentUserId = classSession.Booking?.Student?.Linkeduserid; // UserId thực của student (có thể null với dữ liệu cũ)
        var parentId = classSession.Booking?.Parentid;

        var tutorPresent = !string.IsNullOrEmpty(tutorId) && _presence.IsPresent(classSessionId, tutorId);
        // "Học viên có mặt" = tài khoản student (Linkeduserid) đang trong phòng. Fallback dữ liệu
        // cũ (student chưa link tài khoản): chấp nhận phụ huynh có mặt thay cho học viên.
        var studentPresent =
            (!string.IsNullOrEmpty(studentUserId) && _presence.IsPresent(classSessionId, studentUserId))
            || (string.IsNullOrEmpty(studentUserId) && !string.IsNullOrEmpty(parentId) && _presence.IsPresent(classSessionId, parentId));

        var roomClosed = classSession.Checkouttime.HasValue;
        var isCheckedIn = classSession.Checkintime.HasValue;
        var blockedByPayment = false;

        if (classSession.Status == Scheduled && tutorPresent && studentPresent && !roomClosed)
        {
            // Giữ nguyên rào thanh toán đợt 2: chưa trả thì chưa cho vào buổi tiếp theo.
            if (await IsNextSessionBlockedByRemainingPaymentAsync(classSession.Booking, classSession.Bookingid, classSessionId))
            {
                blockedByPayment = true;
            }
            else
            {
                var now = TimeZoneHelper.UtcNow;
                // Cập nhật có điều kiện (atomic UPDATE ... WHERE status='scheduled'): khi hai
                // heartbeat của gia sư và học viên tới gần như đồng thời, chỉ một request thắng
                // → tránh double check-in mà không cần khoá hàng (điểm yếu của luồng cũ).
                var affected = await _context.ClassSessions
                    .Where(l => l.Classsessionid == classSessionId && l.Status == Scheduled)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(l => l.Status, InProgress)
                        .SetProperty(l => l.Checkintime, now)
                        .SetProperty(l => l.Realstart, now)
                        .SetProperty(l => l.Istutorpresent, true)
                        .SetProperty(l => l.Isstudentpresent, true)
                        .SetProperty(l => l.Meetinglink, l => l.Meetinglink ?? classSessionId.ToString()));

                if (affected == 1)
                {
                    isCheckedIn = true;
                    _logger.LogInformation("Auto check-in classSession {ClassSessionId}: cả gia sư và học viên đã vào phòng", classSessionId);
                    // Đồng bộ entity đã nạp để helper thông báo dùng đúng Meetinglink.
                    classSession.Meetinglink ??= classSessionId.ToString();
                    // ── Tự động bắt đầu Cloud Recording (không chặn check-in nếu lỗi) ──
                    await TryStartRecordingAsync(classSession);
                    await SendClassSessionStartedNotificationsAsync(classSession);
                }
            }
        }

        return new SessionPresenceStatus(
            TutorPresent: tutorPresent,
            StudentPresent: studentPresent,
            IsCheckedIn: isCheckedIn,
            RoomClosed: roomClosed,
            BlockedByPayment: blockedByPayment);
    }

    /// <summary>
    /// Gửi thông báo + tin nhắn chat "buổi học đã bắt đầu" cho phụ huynh và học viên khi buổi
    /// vừa được check-in. Best-effort: mọi lỗi gửi được nuốt và chỉ log cảnh báo.
    /// </summary>
    private async Task SendClassSessionStartedNotificationsAsync(ClassSession classSession)
    {
        var tutorId = classSession.Tutorid;
        if (string.IsNullOrEmpty(tutorId)) return;

        var classSessionId = classSession.Classsessionid;
        var parentId = classSession.Booking?.Parentid;
        var studentProfileId = classSession.Booking?.Studentid; // ProfileId (stu_xxx), KHÔNG phải UserId
        var classSessionTimeVn = classSession.Scheduledstart.ToString("dd/MM HH:mm");
        var hasMeetLink = !string.IsNullOrWhiteSpace(classSession.Meetinglink);

        string chatContent;
        string messageType;
        if (hasMeetLink)
        {
            chatContent = $"🟢 Buổi học đã bắt đầu lúc {classSessionTimeVn}!\n\n🔗 Cả gia sư và học viên đã vào phòng học trực tuyến.";
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
        }

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
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send check-in chat to parent for classSession {ClassSessionId}", classSessionId);
            }

            try
            {
                await _notificationService.CreateNotificationAsync(new NotificationRequest
                {
                    Userid = parentId,
                    Title = "Buổi học đã bắt đầu",
                    Message = "Gia sư và học viên đã vào phòng, buổi học bắt đầu.",
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
                var studentChannelId = await _chatService.GetOrCreateChannelAsync(studentLinkedUserId, tutorId, isStudent: true);
                await _chatService.SendMessageAsync(tutorId, studentChannelId, new ChatMessageCreateRequest
                {
                    Content     = chatContent,
                    MessageType = messageType,
                    Metadata    = chatMetadata
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send check-in chat to student for classSession {ClassSessionId}", classSessionId);
            }

            try
            {
                await _notificationService.CreateNotificationAsync(new NotificationRequest
                {
                    Userid = studentLinkedUserId,
                    Title = "Buổi học đã bắt đầu",
                    Message = "Gia sư và học viên đã vào phòng, buổi học bắt đầu.",
                    Type = NotificationType.LessonCheckin,
                    Referenceid = classSessionId.ToString()
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send check-in notification to student for classSession {ClassSessionId}", classSessionId);
            }
        }
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
    /// Bắt đầu Agora Cloud Recording cho buổi học (nếu tính năng bật).
    /// Lỗi record KHÔNG được làm hỏng check-in → nuốt exception, chỉ log cảnh báo.
    /// </summary>
    private async Task TryStartRecordingAsync(ClassSession classSession)
    {
        if (!_cloudRecording.Enabled) return;
        if (!string.IsNullOrEmpty(classSession.Recordingsid)) return; // đã đang record

        try
        {
            // Recorder phải join ĐÚNG channel riêng của buổi mà client đang dùng.
            var channel = AgoraChannelName.ForSession(classSession.Classsessionid, classSession.Bookingid);
            var handle = await _cloudRecording.StartAsync(classSession.Classsessionid, channel);
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
            var channel = AgoraChannelName.ForSession(classSession.Classsessionid, classSession.Bookingid);
            var result = await _cloudRecording.StopAsync(
                classSession.Classsessionid, channel, classSession.Recordingresourceid, classSession.Recordingsid);

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

            // Yêu cầu buổi đã được check-in (in_progress). Check-in nay là auto khi cả gia sư và
            // học viên cùng vào phòng, nên điều kiện này đảm bảo chỉ những buổi thật sự diễn ra
            // (cả 2 có mặt) mới gửi được báo cáo → mới đi tiếp tới thanh toán.
            if (classSession.Status != InProgress)
                throw new ClassSessionException(ClassSessionErrorCodes.InvalidClassSessionStatus, "Buổi học phải đang diễn ra (đã điểm danh vào) mới gửi được báo cáo", 400);

            if (classSession.ClassSessionReport != null)
                throw new ClassSessionException(ClassSessionErrorCodes.ReportAlreadySubmitted, "Báo cáo buổi học đã được gửi rồi", 400);

            var now = TimeZoneHelper.UtcNow;

            classSession.Lessoncontent = request.ContentCovered;
            classSession.Homework = request.HomeworkAssigned;
            classSession.Tutornotes = request.TutorNotes;
            // Điểm danh có mặt (Istutorpresent/Isstudentpresent) đã được ghi lúc auto check-in từ
            // presence THẬT — không ghi đè bằng giá trị tự khai trong request. Chỉ nhận ghi chú.
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
