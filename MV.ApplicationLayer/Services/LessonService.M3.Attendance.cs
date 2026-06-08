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
using static MV.DomainLayer.Constants.LessonStatus;

namespace MV.ApplicationLayer.Services;

public partial class LessonService
{
    // ── M3-T2: Check-in / Check-out / Report ─────────────────────────────────

    public async Task<LessonDetailResponse> CheckInAsync(int lessonId, string tutorId, CheckInRequest request)
    {
        var lesson = await _context.Lessons
            .Include(l => l.Booking)
            .FirstOrDefaultAsync(l => l.Lessonid == lessonId && l.Tutorid == tutorId)
            ?? throw new LessonException(LessonErrorCodes.LessonNotFound, "Không tìm thấy buổi học", 404);

        if (lesson.Status != Scheduled)
            throw new LessonException(LessonErrorCodes.InvalidLessonStatus, "Buổi học không ở trạng thái đã lên lịch", 400);

        // Block check-in for subsequent lessons if remaining 50% not paid yet
        if (lesson.Booking != null &&
            (lesson.Booking.Status == BookingStatus.DepositPaid || lesson.Booking.Status == BookingStatus.PendingRemainingPayment) &&
            lesson.Booking.Remainingpaidat == null)
        {
            var hasCompletedLesson = await _context.Lessons.AnyAsync(
                l => l.Bookingid == lesson.Bookingid && l.Lessonid != lessonId
                && (l.Status == Completed || l.Status == PendingConfirmation || l.Status == InProgress));

            if (hasCompletedLesson)
                throw new LessonException(BookingErrorCodes.RemainingNotPaid,
                    "Phụ huynh chưa thanh toán 50% còn lại. Vui lòng đợi thanh toán trước khi bắt đầu buổi tiếp theo.", 400);
        }

        var now = TimeZoneHelper.UtcNow;
        var minutesDiff = Math.Abs((now - lesson.Scheduledstart).TotalMinutes);
        if (minutesDiff > 15)
            throw new LessonException(LessonErrorCodes.CheckInTooEarly, "Chỉ được điểm danh trong vòng ±15 phút so với giờ bắt đầu", 400);

        lesson.Checkintime = now;
        lesson.Realstart = now;
        lesson.Status = InProgress;
        lesson.Istutorpresent = true;

        await _context.SaveChangesAsync();
        _logger.LogInformation("Tutor {TutorId} checked in to lesson {LessonId}", tutorId, lessonId);

        // ── Tạo Meet link nếu chưa có (online/hybrid) ──
        var teachingMode = TeachingMode.Online;
        var isOnlineMode = true;

        _logger.LogInformation("CheckIn lesson {LessonId}: teachingMode={Mode}, isOnlineMode={IsOnline}, hasMeetLink={HasLink}, studentId={StudentId}",
            lessonId, teachingMode, isOnlineMode, !string.IsNullOrWhiteSpace(lesson.Meetinglink), lesson.Booking?.Studentid);

        if (isOnlineMode && string.IsNullOrWhiteSpace(lesson.Meetinglink) && lesson.Booking?.Studentid != null)
        {
            // Tencent RTC: RoomId = lessonId (không cần OAuth, không cần external API)
            lesson.Meetinglink = lessonId.ToString();
            _logger.LogInformation("Assigned Tencent RTC RoomId {RoomId} for lesson {LessonId}", lessonId, lessonId);
            await _context.SaveChangesAsync();
        }


        // ── Gửi thông báo + link vào chat cho Parent và Student ──
        var parentId = lesson.Booking?.Parentid;
        var studentProfileId = lesson.Booking?.Studentid; // ProfileId (stu_xxx), KHÔNG phải UserId
        var lessonTimeVn = TimeZoneHelper.ToUserTime(lesson.Scheduledstart).ToString("dd/MM HH:mm");
        // hasMeetLink được tính lại SAU khi Tencent RTC RoomId đã được set
        var hasMeetLink = !string.IsNullOrWhiteSpace(lesson.Meetinglink);
        string chatContent;
        string messageType;
        if (hasMeetLink)
        {
            chatContent = $"🟢 Buổi học đã bắt đầu lúc {lessonTimeVn}!\n\n🔗 Link tham gia (Tencent RTC - Room ID: {lesson.Meetinglink}):\nĐể vào phòng học, vui lòng mở ứng dụng và nhập Room ID: {lesson.Meetinglink}";
            messageType = ChatMessageType.MeetLink;
        }
        else
        {
            chatContent = $"🟢 Buổi học đã bắt đầu lúc {lessonTimeVn}.";
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

            _logger.LogInformation("CheckIn lesson {LessonId}: studentProfileId={ProfileId}, studentLinkedUserId={LinkedId}",
                lessonId, studentProfileId, studentLinkedUserId ?? "null");
        }

        // Chuẩn bị metadata chat
        var chatMetadata = new
        {
            lessonId,
            meetingLink = lesson.Meetinglink,
            scheduledStart = TimeZoneHelper.ToUserTime(lesson.Scheduledstart)
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
                _logger.LogInformation("Sent check-in chat to parent channel {ChannelId} for lesson {LessonId}", parentChannelId, lessonId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send check-in chat to parent for lesson {LessonId}", lessonId);
            }

            // Push notification cho Parent
            try
            {
                await _notificationService.CreateNotificationAsync(new NotificationRequest
                {
                    Userid = parentId,
                    Title = "Buổi học đã bắt đầu",
                    Message = "Gia sư đã bắt đầu buổi học." +
                                  (hasMeetLink ? " Kiểm tra tin nhắn để lấy Room ID tham gia Tencent RTC." : ""),
                    Type = NotificationType.LessonCheckin,
                    Referenceid = lessonId.ToString()
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send check-in notification to parent for lesson {LessonId}", lessonId);
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
                _logger.LogInformation("Sent check-in chat to student channel {ChannelId} for lesson {LessonId}", studentChannelId, lessonId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send check-in chat to student for lesson {LessonId}", lessonId);
            }

            // Push notification cho Student (dùng linkedUserId)
            try
            {
                await _notificationService.CreateNotificationAsync(new NotificationRequest
                {
                    Userid = studentLinkedUserId,
                    Title = "Buổi học đã bắt đầu",
                    Message = "Gia sư đã bắt đầu buổi học." +
                                  (hasMeetLink ? " Kiểm tra tin nhắn để lấy Room ID tham gia Tencent RTC." : ""),
                    Type = NotificationType.LessonCheckin,
                    Referenceid = lessonId.ToString()
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send check-in notification to student for lesson {LessonId}", lessonId);
            }
        }

        return (await GetTutorLessonDetailAsync(lessonId, tutorId))!;
    }

    public async Task<LessonDetailResponse> CheckOutAsync(int lessonId, string tutorId, CheckOutRequest request)
    {
        var lesson = await _context.Lessons
            .Include(l => l.Booking)
            .FirstOrDefaultAsync(l => l.Lessonid == lessonId && l.Tutorid == tutorId)
            ?? throw new LessonException(LessonErrorCodes.LessonNotFound, "Không tìm thấy buổi học", 404);

        if (lesson.Status != InProgress)
            throw new LessonException(LessonErrorCodes.InvalidLessonStatus, "Buổi học không ở trạng thái đang diễn ra", 400);

        if (!lesson.Checkintime.HasValue)
            throw new LessonException(LessonErrorCodes.NotCheckedIn, "Vui lòng điểm danh vào trước", 400);

        lesson.Checkouttime = TimeZoneHelper.UtcNow;
        lesson.Realend = TimeZoneHelper.UtcNow;

        await _context.SaveChangesAsync();
        _logger.LogInformation("Tutor {TutorId} checked out from lesson {LessonId}", tutorId, lessonId);

        return (await GetTutorLessonDetailAsync(lessonId, tutorId))!;
    }

    public async Task<LessonDetailResponse> SubmitReportAsync(int lessonId, string tutorId, SubmitReportRequest request)
    {
        await using var tx = await _context.Database.BeginTransactionAsync();
        try
        {
            var lesson = await _context.Lessons
                .Include(l => l.Booking)
                    .ThenInclude(b => b!.Student)
                .Include(l => l.Lessonreport)
                .FirstOrDefaultAsync(l => l.Lessonid == lessonId && l.Tutorid == tutorId)
                ?? throw new LessonException(LessonErrorCodes.LessonNotFound, "Không tìm thấy buổi học", 404);

            // MVP Phase 2: Bỏ check-in/check-out bắt buộc

            if (lesson.Status != Scheduled && lesson.Status != InProgress)
                throw new LessonException(LessonErrorCodes.InvalidLessonStatus, "Buổi học phải ở trạng thái đã lên lịch hoặc đang diễn ra mới gửi được báo cáo", 400);

            if (lesson.Lessonreport != null)
                throw new LessonException(LessonErrorCodes.ReportAlreadySubmitted, "Báo cáo buổi học đã được gửi rồi", 400);

            var now = TimeZoneHelper.UtcNow;

            if (lesson.Status == Scheduled)
            {
                lesson.Isearlysubmission = true;
            }

            lesson.Lessoncontent = request.ContentCovered;
            lesson.Homework = request.HomeworkAssigned;
            lesson.Tutornotes = request.TutorNotes;
            lesson.Istutorpresent = request.IsTutorPresent;
            lesson.Isstudentpresent = request.IsStudentPresent;
            lesson.Attendancenote = request.AttendanceNote;
            lesson.Status = PendingConfirmation;
            lesson.Submittedat = now;
            lesson.Confirmdeadline = now.AddHours(24);

            var report = new Lessonreport
            {
                Lessonid = lessonId,
                Createdbytutorid = tutorId,
                Contentcovered = request.ContentCovered,
                Homeworkassigned = request.HomeworkAssigned,
                Studentperformancerating = request.StudentPerformanceRating,
                Attachments = request.Attachments != null ? JsonSerializer.Serialize(request.Attachments) : null,
                Createdat = now
            };
            _context.Lessonreports.Add(report);

            await _context.SaveChangesAsync();
            await tx.CommitAsync();

            _logger.LogInformation("Tutor {TutorId} submitted report for lesson {LessonId}", tutorId, lessonId);

            // Notify Parent
            var parentId = lesson.Booking?.Student?.Parentid;
            if (!string.IsNullOrEmpty(parentId))
            {
                await _notificationService.CreateNotificationAsync(new NotificationRequest
                {
                    Userid = parentId,
                    Title = "Báo cáo buổi học mới",
                    Message = $"Gia sư đã gửi báo cáo cho buổi học #{lessonId}. Vui lòng kiểm tra và xác nhận trong vòng 24h.",
                    Type = NotificationType.LessonReport,
                    Referenceid = lessonId.ToString()
                });
            }

            // If this is the first lesson report for a deposit-only booking,
            // notify parent that remaining 50% will be required after 24h confirmation period
            if (lesson.Booking != null && lesson.Booking.Status == BookingStatus.DepositPaid
                && lesson.Booking.Remainingpaidat == null && !string.IsNullOrEmpty(parentId))
            {
                var isFirstReport = !await _context.Lessons.AnyAsync(
                    l => l.Bookingid == lesson.Bookingid && l.Lessonid != lessonId
                    && (l.Status == PendingConfirmation || l.Status == Completed));

                if (isFirstReport)
                {
                    await _notificationService.CreateNotificationAsync(new NotificationRequest
                    {
                        Userid = parentId,
                        Title = "Sắp cần thanh toán 50% còn lại",
                        Message = $"Buổi học đầu tiên của booking #{lesson.Bookingid} đã hoàn thành. " +
                            $"Sau 24h xác nhận, bạn sẽ cần thanh toán 50% còn lại ({lesson.Booking.Remainingamount:N0}đ) để tiếp tục các buổi học sau."
                    });
                }
            }

            return (await GetTutorLessonDetailAsync(lessonId, tutorId))!;
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    public async Task<string> UploadAttachmentAsync(int lessonId, string tutorId, IFormFile file)
    {
        var lesson = await _context.Lessons
            .FirstOrDefaultAsync(l => l.Lessonid == lessonId && l.Tutorid == tutorId)
            ?? throw new LessonException(LessonErrorCodes.LessonNotFound, "Không tìm thấy buổi học", 404);

        // Ensure bucket exists
        await _storageService.EnsureBucketExistsAsync(LessonAttachmentBucket);

        // Upload to Cloudinary Storage using lesson-specific folder
        var folderPath = $"lesson-{lessonId}";
        var fileUrl = await _storageService.UploadFileAsync(LessonAttachmentBucket, folderPath, file);

        _logger.LogInformation("Uploaded attachment {FileName} for lesson {LessonId} → {Url}", file.FileName, lessonId, fileUrl);
        return fileUrl;
    }
}
