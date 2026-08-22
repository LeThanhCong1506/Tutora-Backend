using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.Helpers;
using MV.ApplicationLayer.Services.Agora;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO;
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
            return new SessionPresenceStatus(false, false, false, false, false, false);

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
        SessionScheduleConflictResponse? scheduleConflict = null;
        var now = TimeZoneHelper.UtcNow;

        // A delayed/reconnected heartbeat must never start an old scheduled
        // session. Keep the record scheduled so the normal no-show/dispute
        // workflow remains available, but tell both clients that its room is closed.
        if (classSession.Status == Scheduled
            && classSession.Scheduledend <= now.AddMinutes(-LiveSessionAutoEndGraceMinutes))
        {
            return new SessionPresenceStatus(
                TutorPresent: tutorPresent,
                StudentPresent: studentPresent,
                IsCheckedIn: false,
                RoomClosed: true,
                BlockedByPayment: false,
                IsRecording: false);
        }

        if (classSession.Status == Scheduled && tutorPresent && studentPresent && !roomClosed)
        {
            // Giữ nguyên rào thanh toán đợt 2: chưa trả thì chưa cho vào buổi tiếp theo.
            if (await IsNextSessionBlockedByRemainingPaymentAsync(classSession.Booking, classSession.Bookingid, classSessionId))
            {
                blockedByPayment = true;
            }
            else
            {
                var approvedChange = await _context.ClassSessionScheduleChanges
                    .Where(x => x.Classsessionid == classSessionId
                        && x.Status == ScheduleChangeStatus.Approved
                        && x.Expiresat > now)
                    .OrderByDescending(x => x.Schedulechangeid)
                    .FirstOrDefaultAsync();

                int affected;
                if (approvedChange != null)
                {
                    var adjustedEnd = now.Add(
                        approvedChange.Originalscheduledend - approvedChange.Originalscheduledstart);
                    await using var scheduleTransaction = _context.Database.IsRelational()
                        ? await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable)
                        : null;

                    scheduleConflict = await ClassSessionScheduleConflictGuard.FindAsync(
                        _context,
                        classSessionId,
                        classSession.Tutorid,
                        classSession.Studentid,
                        now,
                        adjustedEnd,
                        currentApprovedAt: approvedChange.Approvedat,
                        currentScheduleChangeId: approvedChange.Schedulechangeid);

                    if (scheduleConflict != null)
                    {
                        affected = 0;
                    }
                    else
                    {
                        affected = await _context.ClassSessions
                            .Where(l => l.Classsessionid == classSessionId && l.Status == Scheduled)
                            .ExecuteUpdateAsync(s => s
                                .SetProperty(l => l.Status, InProgress)
                                .SetProperty(l => l.Checkintime, now)
                                .SetProperty(l => l.Realstart, now)
                                .SetProperty(l => l.Scheduledstart, now)
                                .SetProperty(l => l.Scheduledend, adjustedEnd)
                                .SetProperty(l => l.Istutorpresent, true)
                                .SetProperty(l => l.Isstudentpresent, true)
                                .SetProperty(l => l.Meetinglink, l => l.Meetinglink ?? classSessionId.ToString()));

                        if (affected == 1)
                        {
                            approvedChange.Status = ScheduleChangeStatus.Applied;
                            approvedChange.Appliedat = now;
                            approvedChange.Adjustedscheduledstart = now;
                            approvedChange.Adjustedscheduledend = adjustedEnd;
                            approvedChange.Updatedat = now;
                            await _context.SaveChangesAsync();
                            classSession.Scheduledstart = now;
                            classSession.Scheduledend = adjustedEnd;
                        }
                    }

                    if (scheduleTransaction != null)
                        await scheduleTransaction.CommitAsync();
                }
                else
                {
                    // Atomic status transition keeps concurrent tutor/student heartbeats idempotent.
                    affected = await _context.ClassSessions
                        .Where(l => l.Classsessionid == classSessionId && l.Status == Scheduled)
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(l => l.Status, InProgress)
                            .SetProperty(l => l.Checkintime, now)
                            .SetProperty(l => l.Realstart, now)
                            .SetProperty(l => l.Istutorpresent, true)
                            .SetProperty(l => l.Isstudentpresent, true)
                            .SetProperty(l => l.Meetinglink, l => l.Meetinglink ?? classSessionId.ToString()));
                }

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

        // Ghi hình đang chạy khi đã có Sid (được TryStartRecordingAsync gán, hoặc nạp sẵn từ DB
        // nếu buổi record ở nhịp heartbeat trước). Tính sau khối auto check-in để bắt cả 2 trường hợp.
        var isRecording = !string.IsNullOrEmpty(classSession.Recordingsid) && !roomClosed;

        // Mốc hệ thống sẽ tự đóng phòng nếu chưa ai kết thúc — chỉ có ý nghĩa khi buổi đang
        // diễn ra thật (đã check-in) và chưa đóng. Dùng Scheduledend đã nạp (có thể vừa được
        // điều chỉnh theo lịch đổi giờ được duyệt ở khối auto check-in phía trên).
        DateTime? autoEndAt = (isCheckedIn && !roomClosed)
            ? classSession.Scheduledend.AddMinutes(LiveSessionAutoEndGraceMinutes)
            : null;

        return new SessionPresenceStatus(
            TutorPresent: tutorPresent,
            StudentPresent: studentPresent,
            IsCheckedIn: isCheckedIn,
            RoomClosed: roomClosed,
            BlockedByPayment: blockedByPayment,
            IsRecording: isRecording,
            ScheduleConflict: scheduleConflict,
            AutoEndAt: autoEndAt);
    }

    /// <summary>
    /// Gửi thông báo "buổi học đã bắt đầu" cho phụ huynh và học viên khi buổi vừa được
    /// check-in. Chỉ gửi qua Notification (KHÔNG còn gửi tin nhắn vào chat) — realtime qua
    /// NotificationHub + FCM push. Best-effort: mọi lỗi gửi được nuốt và chỉ log cảnh báo.
    /// </summary>
    private async Task SendClassSessionStartedNotificationsAsync(ClassSession classSession)
    {
        var tutorId = classSession.Tutorid;
        if (string.IsNullOrEmpty(tutorId)) return;

        var classSessionId = classSession.Classsessionid;
        var parentId = classSession.Booking?.Parentid;
        var studentProfileId = classSession.Booking?.Studentid; // ProfileId (stu_xxx), KHÔNG phải UserId

        // Resolve Student LinkedUserId (UserId thực sự để tạo notification)
        string? studentLinkedUserId = null;
        if (!string.IsNullOrEmpty(studentProfileId))
        {
            studentLinkedUserId = await _context.Studentprofiles
                .Where(s => s.Studentid == studentProfileId)
                .Select(s => s.Linkeduserid)
                .FirstOrDefaultAsync();
        }

        // ── Gửi cho Parent ──
        if (!string.IsNullOrEmpty(parentId))
        {
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
    /// Thời gian ân hạn sau giờ kết thúc dự kiến trước khi hệ thống tự đóng phòng — đề phòng
    /// buổi học kéo dài thêm chút ít nhưng không ai bấm "Kết thúc". Sau mốc này, phòng bị đóng
    /// (Checkouttime) dù gia sư/học viên chưa tự rời — cả 2 phía tự động bị đá ra ở nhịp heartbeat
    /// kế tiếp (≤20s, xem RoomClosed trong TryAutoCheckInAsync và sendHeartbeat ở FE) thay vì mỗi
    /// người tự rời lúc khác nhau.
    /// </summary>
    public const int LiveSessionAutoEndGraceMinutes = 30;

    public async Task<int> AutoCloseExpiredLiveSessionsAsync(CancellationToken ct = default)
    {
        var now = TimeZoneHelper.UtcNow;
        var cutoff = now.AddMinutes(-LiveSessionAutoEndGraceMinutes);

        var expiredSessions = await _context.ClassSessions
            .Where(l => l.Status == InProgress
                && l.Checkouttime == null
                && l.Scheduledend <= cutoff)
            .ToListAsync(ct);

        if (expiredSessions.Count == 0) return 0;

        foreach (var classSession in expiredSessions)
        {
            classSession.Checkouttime = now;
            classSession.Realend = now;

            await TryStopRecordingAsync(classSession);

            _logger.LogInformation(
                "Tự động đóng phòng buổi học {ClassSessionId}: đã quá {GraceMinutes} phút sau giờ kết thúc dự kiến {ScheduledEnd:o}.",
                classSession.Classsessionid, LiveSessionAutoEndGraceMinutes, classSession.Scheduledend);
        }

        await _context.SaveChangesAsync(ct);
        return expiredSessions.Count;
    }

    /// <summary>
    /// Buổi học bị ngắt giữa chừng chỉ được phép có buổi phụ trong đúng ngày bị ngắt (xem
    /// <see cref="RequestInterruptionAsync"/> và guard tương ứng trong
    /// ClassSessionRescheduleProposalService.ProposeAsync). Nếu qua nửa đêm của ngày đó
    /// (Interruptedat.Date + 1 ngày, UTC thuần) mà vẫn chưa xử lý xong, tự đóng: buổi gốc được
    /// settle qua đường bỏ-qua-status-guard mà dispute đang dùng (SettleDisputedClassSessionAsync)
    /// nên chuyển thẳng sang Completed và trừ Sessionsremaining đúng 1 lần dù đang ở Interrupted
    /// (trạng thái mà SettleClassSessionAsync bình thường sẽ từ chối); buổi phụ chưa dùng (nếu có,
    /// còn Scheduled) chuyển sang Cancelled để không còn nằm "lơ lửng" trên dashboard.
    /// </summary>
    public async Task<int> AutoCloseExpiredInterruptedSessionsAsync(CancellationToken ct = default)
    {
        var now = TimeZoneHelper.UtcNow;

        var candidates = await _context.ClassSessions
            .Include(l => l.Booking)
                .ThenInclude(b => b!.Student)
            .Where(l => l.Status == Interrupted && l.Interruptedat != null)
            .ToListAsync(ct);

        var expired = candidates.Where(l => now >= l.Interruptedat!.Value.Date.AddDays(1)).ToList();
        if (expired.Count == 0) return 0;

        var closedCount = 0;
        foreach (var original in expired)
        {
            try
            {
                // SettleDisputedClassSessionAsync tự mở transaction Serializable riêng của nó —
                // không bọc thêm transaction ở đây để tránh nested-transaction.
                await _settlementService.SettleDisputedClassSessionAsync(original.Classsessionid);

                var unusedContinuation = await _context.ClassSessions.FirstOrDefaultAsync(c =>
                    c.Originalsessionid == original.Classsessionid
                    && c.Iscontinuation
                    && c.Status == Scheduled, ct);
                if (unusedContinuation != null)
                {
                    unusedContinuation.Status = Cancelled;
                    await _context.SaveChangesAsync(ct);
                }

                closedCount++;

                try
                {
                    var studentUserId = original.Booking?.Student?.Linkeduserid;
                    var parentId = original.Booking?.Student?.Parentid ?? original.Booking?.Parentid;
                    var tutorMessage = $"Buổi học #{original.Classsessionid} bị ngắt giữa chừng và đã quá thời hạn học tiếp trong ngày. Hệ thống đã tự động ghi nhận hoàn tất buổi học.";
                    var notifications = new List<NotificationRequest>();
                    if (!string.IsNullOrWhiteSpace(original.Tutorid))
                        notifications.Add(new NotificationRequest
                        {
                            Userid = original.Tutorid,
                            Title = "Buổi học bị ngắt đã tự động hoàn tất",
                            Message = tutorMessage,
                            Type = NotificationType.LessonInterruptionAutoClosed,
                            Referenceid = original.Classsessionid.ToString()
                        });
                    if (!string.IsNullOrWhiteSpace(studentUserId))
                        notifications.Add(new NotificationRequest
                        {
                            Userid = studentUserId,
                            Title = "Buổi học bị ngắt đã tự động hoàn tất",
                            Message = tutorMessage,
                            Type = NotificationType.LessonInterruptionAutoClosed,
                            Referenceid = original.Classsessionid.ToString()
                        });
                    if (!string.IsNullOrWhiteSpace(parentId))
                        notifications.Add(new NotificationRequest
                        {
                            Userid = parentId,
                            Title = "Buổi học bị ngắt đã tự động hoàn tất",
                            Message = $"Buổi học #{original.Classsessionid} của con bạn bị ngắt giữa chừng và đã quá thời hạn học tiếp trong ngày. Hệ thống đã tự động ghi nhận hoàn tất buổi học.",
                            Type = NotificationType.LessonInterruptionAutoClosed,
                            Referenceid = original.Classsessionid.ToString()
                        });

                    if (notifications.Count > 0)
                        await _notificationService.CreateNotificationsAsync(notifications);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Không thể gửi thông báo tự đóng buổi học bị ngắt {ClassSessionId}", original.Classsessionid);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Không thể tự đóng buổi học bị ngắt {ClassSessionId} đã quá ngày.", original.Classsessionid);
            }
        }

        return closedCount;
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

            // Stop trả về 2xx nhưng không có file nào (recorder đã tự thoát vì channel trống,
            // hoặc không bắt được stream): bản ghi coi như hỏng. Không có gì để relay, và
            // RecordingStatusResolver sẽ báo "failed" nhờ sid còn lại — phải log ở mức Error
            // thì mới lần ra được, vì check-out vẫn thành công như thường.
            if (mp4Key == null)
            {
                _logger.LogError(
                    "Cloud Recording buổi học {ClassSessionId} dừng nhưng Agora không trả về file nào (sid={Sid}) — bản ghi coi như hỏng.",
                    classSession.Classsessionid, classSession.Recordingsid);
            }

            classSession.Recordings3key = mp4Key;            // job relay sẽ đẩy lên Drive rồi xóa file S3
            classSession.Recordingurl = result.PlaybackUrl;  // link S3 tạm (nếu có PublicUrlBase)
            await _context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            // Nuốt lỗi để không hỏng check-out, nhưng đây là mất bản ghi thật sự → Error, không phải Warning.
            _logger.LogError(ex, "Không thể dừng Cloud Recording cho buổi học {ClassSessionId}", classSession.Classsessionid);
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
            //
            // Ngoại lệ: buổi bị báo ngắt (status=interrupted) không bao giờ tự quay lại in_progress
            // được nữa (xem RequestInterruptionAsync), nên nếu không xử lý riêng, gia sư sẽ vĩnh
            // viễn không gửi được báo cáo cho phần ĐÃ dạy thật (VD 80%) khi 2 bên đồng ý bỏ hẳn
            // buổi phụ thay vì học nốt. Cho phép gửi báo cáo khi CẢ 2 bên đã xác nhận bỏ buổi phụ
            // (ConfirmSkipContinuationAsync) — đồng thời tự huỷ buổi phụ đó trong cùng transaction.
            ClassSession? continuationToCancel = null;
            if (classSession.Status == Interrupted)
            {
                continuationToCancel = await _context.ClassSessions.FirstOrDefaultAsync(
                    c => c.Originalsessionid == classSessionId && c.Iscontinuation && c.Status == Scheduled);
            }
            var bothSidesSkippedContinuation = continuationToCancel?.Tutorskipconfirmedat != null
                && continuationToCancel.Studentskipconfirmedat != null;

            if (classSession.Status != InProgress && !bothSidesSkippedContinuation)
                throw new ClassSessionException(
                    ClassSessionErrorCodes.InvalidClassSessionStatus,
                    "Buổi học phải đang diễn ra (đã điểm danh vào), hoặc đã bị ngắt và cả 2 bên đã đồng ý bỏ buổi phụ, mới gửi được báo cáo",
                    400);

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
            classSession.Confirmdeadline = now.AddHours(12);

            var report = new ClassSessionReport
            {
                Classsessionid = classSessionId,
                Createdbytutorid = tutorId,
                Contentcovered = request.ContentCovered,
                Homeworkassigned = request.HomeworkAssigned,
                Studentperformancerating = request.StudentPerformanceRating,
                // Client mới gửi AttachmentDetails (có mô tả); client cũ chỉ gửi mảng URL.
                Attachments = ReportAttachmentSerializer.Serialize(
                    request.AttachmentDetails
                    ?? request.Attachments?.Select(url => new ReportAttachment { Url = url })),
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
                // Không để hạn 48h vượt quá giờ học buổi reserved gần nhất — xem
                // RemainingPaymentDeadlinePolicy để biết lý do.
                var earliestReservedStart = await _context.ClassSessions
                    .Where(x => x.Bookingid == classSession.Bookingid && x.Status == Reserved)
                    .OrderBy(x => x.Scheduledstart)
                    .Select(x => (DateTime?)x.Scheduledstart)
                    .FirstOrDefaultAsync();

                classSession.Booking!.Status = BookingStatus.PendingRemainingPayment;
                classSession.Booking.Paymentdueat = RemainingPaymentDeadlinePolicy.ComputeDeadline(now, earliestReservedStart);
            }

            if (bothSidesSkippedContinuation)
                continuationToCancel!.Status = Cancelled;

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

    /// <summary>
    /// Gia sư hoặc học viên/phụ huynh báo buổi học phải ngắt giữa chừng vì sự cố đột xuất. Chỉ cho
    /// phép khi đã học đủ ngưỡng % tối thiểu (xem <see cref="ClassSessionInterruptionPolicy"/>) —
    /// ngưỡng là điều kiện chống lạm dụng, không phải % dùng để tính thời lượng buổi phụ. Buổi gốc
    /// chuyển sang <c>interrupted</c> (trạng thái cụt, không tự đi tới pending_confirmation/completed
    /// nên không tự trừ Sessionsremaining); đồng thời sinh 1 buổi phụ (Iscontinuation=true) cùng
    /// booking/tutor/student để học nốt trong ngày.
    /// </summary>
    public async Task<ClassSessionDetailResponse> RequestInterruptionAsync(int classSessionId, string requestingUserId, string? reason)
    {
        await using var tx = await _context.Database.BeginTransactionAsync();
        try
        {
            var classSession = await _context.ClassSessions
                .Include(l => l.Booking)
                    .ThenInclude(b => b!.Student)
                .FirstOrDefaultAsync(l => l.Classsessionid == classSessionId)
                ?? throw new ClassSessionException(ClassSessionErrorCodes.ClassSessionNotFound, "Không tìm thấy buổi học", 404);

            var studentUserId = classSession.Booking?.Student?.Linkeduserid;
            var parentId = classSession.Booking?.Student?.Parentid ?? classSession.Booking?.Parentid;
            var isAuthorized = requestingUserId == classSession.Tutorid
                || (!string.IsNullOrEmpty(studentUserId) && requestingUserId == studentUserId)
                || (!string.IsNullOrEmpty(parentId) && requestingUserId == parentId);
            if (!isAuthorized)
                throw new UnauthorizedAccessException("Bạn không có quyền báo ngắt buổi học này.");

            if (classSession.Status != InProgress)
                throw new ClassSessionException(ClassSessionErrorCodes.InvalidClassSessionStatus, "Buổi học phải đang diễn ra (đã điểm danh vào) mới báo ngắt được", 400);

            if (classSession.Iscontinuation)
                throw new ClassSessionException(ClassSessionErrorCodes.AlreadyContinuationSession, "Buổi học phụ không thể tiếp tục bị báo ngắt", 400);

            if (classSession.Isdisputerelearn)
                throw new ClassSessionException(ClassSessionErrorCodes.AlreadyRelearnSession, "Buổi học lại do hoà giải không thể tiếp tục bị báo ngắt", 400);

            var isFirstSessionOfBooking = await ClassSessionInterruptionPolicy.IsFirstOriginalSessionAsync(_context, classSession);

            var sessionLog = await _sessionLogService.GetSessionLogAsync(classSessionId);
            var overlapRatio = sessionLog?.Summary.OverlapRatio ?? 0.0;
            if (!ClassSessionInterruptionPolicy.MeetsThreshold(isFirstSessionOfBooking, overlapRatio))
            {
                var threshold = ClassSessionInterruptionPolicy.ThresholdFor(isFirstSessionOfBooking);
                throw new ClassSessionException(
                    ClassSessionErrorCodes.InterruptionThresholdNotMet,
                    $"Buổi học cần đạt tối thiểu {threshold:P0} thời lượng mới được phép báo ngắt giữa chừng (hiện tại khoảng {overlapRatio:P0}).",
                    400);
            }

            var now = TimeZoneHelper.UtcNow;
            classSession.Status = Interrupted;
            classSession.Interruptedat = now;
            classSession.Interruptreason = reason;
            classSession.Interruptedby = requestingUserId;
            classSession.Checkouttime = now;
            classSession.Realend = now;

            await TryStopRecordingAsync(classSession);

            var continuation = ClassSessionInterruptionPolicy.BuildContinuationSession(classSession, isFirstSessionOfBooking, now);
            _context.ClassSessions.Add(continuation);

            await _context.SaveChangesAsync();

            // Meetinglink KHÔNG phải link thật — toàn hệ thống dùng chính Classsessionid làm "cờ đã
            // kích hoạt vào học online" (xem ClassSessionService.cs/PaymentService.Wallet.cs), và
            // FE (canJoinLiveSession) ẩn hẳn nút "Vào lớp" nếu thiếu field này. Chỉ biết được giá trị
            // này SAU khi save lần đầu (PK tự tăng), nên phải set + save thêm 1 lần nữa ở đây.
            continuation.Meetinglink = continuation.Classsessionid.ToString();
            await _context.SaveChangesAsync();
            await tx.CommitAsync();

            _logger.LogInformation(
                "ClassSession {ClassSessionId} bị ngắt giữa chừng bởi {UserId} (isFirstSessionOfBooking={IsFirst}, overlapRatio={OverlapRatio:P0}); tạo buổi phụ mới.",
                classSessionId, requestingUserId, isFirstSessionOfBooking, overlapRatio);

            try
            {
                var tutorMessage = $"Buổi học #{classSessionId} đã bị ngắt giữa chừng. Buổi học phụ #{continuation.Classsessionid} đã được tạo để học tiếp phần còn lại trong ngày hôm nay.";
                var notifications = new List<NotificationRequest>();
                if (!string.IsNullOrWhiteSpace(classSession.Tutorid))
                    notifications.Add(new NotificationRequest
                    {
                        Userid = classSession.Tutorid,
                        Title = "Đã tạo buổi học phụ",
                        Message = tutorMessage,
                        Type = NotificationType.LessonContinuationCreated,
                        Referenceid = continuation.Classsessionid.ToString()
                    });
                if (!string.IsNullOrWhiteSpace(studentUserId))
                    notifications.Add(new NotificationRequest
                    {
                        Userid = studentUserId,
                        Title = "Đã tạo buổi học phụ",
                        Message = tutorMessage,
                        Type = NotificationType.LessonContinuationCreated,
                        Referenceid = continuation.Classsessionid.ToString()
                    });
                if (!string.IsNullOrWhiteSpace(parentId))
                    notifications.Add(new NotificationRequest
                    {
                        Userid = parentId,
                        Title = "Đã tạo buổi học phụ",
                        Message = $"Buổi học #{classSessionId} của con bạn bị ngắt giữa chừng do sự cố đột xuất. Đã tạo buổi học phụ #{continuation.Classsessionid} để học tiếp phần còn lại trong ngày hôm nay.",
                        Type = NotificationType.LessonContinuationCreated,
                        Referenceid = continuation.Classsessionid.ToString()
                    });

                if (notifications.Count > 0)
                    await _notificationService.CreateNotificationsAsync(notifications);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Không thể gửi thông báo tạo buổi phụ cho classSession {ClassSessionId}", classSessionId);
            }

            return (await GetTutorClassSessionDetailAsync(classSessionId, classSession.Tutorid!))!;
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<ClassSessionInterruptionEligibilityResponse> GetInterruptionEligibilityAsync(int classSessionId, string requestingUserId)
    {
        var classSession = await _context.ClassSessions
            .Include(l => l.Booking)
                .ThenInclude(b => b!.Student)
            .FirstOrDefaultAsync(l => l.Classsessionid == classSessionId)
            ?? throw new ClassSessionException(ClassSessionErrorCodes.ClassSessionNotFound, "Không tìm thấy buổi học", 404);

        var studentUserId = classSession.Booking?.Student?.Linkeduserid;
        var parentId = classSession.Booking?.Student?.Parentid ?? classSession.Booking?.Parentid;
        var isAuthorized = requestingUserId == classSession.Tutorid
            || (!string.IsNullOrEmpty(studentUserId) && requestingUserId == studentUserId)
            || (!string.IsNullOrEmpty(parentId) && requestingUserId == parentId);
        if (!isAuthorized)
            throw new UnauthorizedAccessException("Bạn không có quyền xem thông tin buổi học này.");

        // Buổi phụ/buổi học lại do hoà giải KHÔNG BAO GIỜ báo ngắt được (dù học bao lâu) — khác với
        // "chưa in_progress", vốn chỉ tạm thời chưa đủ điều kiện chứ vẫn có thể đổi thành true sau.
        var canEverBeInterrupted = !classSession.Iscontinuation && !classSession.Isdisputerelearn;

        // Không throw khi buổi chưa in_progress hoặc thuộc 2 loại trên — trả Eligible=false thẳng,
        // vì đây chỉ là endpoint tra trạng thái cho FE hiện nút, không phải hành động.
        if (classSession.Status != InProgress || !canEverBeInterrupted)
        {
            return new ClassSessionInterruptionEligibilityResponse
            {
                Eligible = false,
                CurrentRatio = 0.0,
                RequiredRatio = ClassSessionInterruptionPolicy.ThresholdFor(false),
                CanEverBeInterrupted = canEverBeInterrupted
            };
        }

        var isFirstSessionOfBooking = await ClassSessionInterruptionPolicy.IsFirstOriginalSessionAsync(_context, classSession);
        var sessionLog = await _sessionLogService.GetSessionLogAsync(classSessionId);
        var overlapRatio = sessionLog?.Summary.OverlapRatio ?? 0.0;
        var requiredRatio = ClassSessionInterruptionPolicy.ThresholdFor(isFirstSessionOfBooking);

        return new ClassSessionInterruptionEligibilityResponse
        {
            Eligible = ClassSessionInterruptionPolicy.MeetsThreshold(isFirstSessionOfBooking, overlapRatio),
            CurrentRatio = overlapRatio,
            RequiredRatio = requiredRatio
        };
    }

    /// <summary>Trạng thái đồng ý bỏ buổi phụ hiện tại — dùng để FE hiện đúng "đang chờ bên kia" /
    /// "cả 2 đã đồng ý". Không yêu cầu quyền riêng gì thêm ngoài xác thực (giống GetInterruptionEligibilityAsync).</summary>
    public async Task<ClassSessionSkipContinuationResponse> GetSkipContinuationStatusAsync(int continuationSessionId, string requestingUserId)
    {
        var classSession = await LoadContinuationForSkipAsync(continuationSessionId, requestingUserId);
        return ToSkipResponse(classSession);
    }

    /// <summary>Gia sư HOẶC học sinh/phụ huynh xác nhận đồng ý bỏ hẳn buổi phụ này (không học nốt
    /// phần còn lại) — mỗi phía tự xác nhận qua đúng cột của mình, idempotent (bấm lại không đổi
    /// mốc giờ đã ghi). Khi cả 2 cùng xác nhận, SubmitReportAsync trên buổi GỐC mới chấp nhận báo
    /// cáo và tự huỷ buổi phụ này — hàm này CHỈ ghi nhận đồng ý, không tự huỷ gì ở đây.</summary>
    public async Task<ClassSessionSkipContinuationResponse> ConfirmSkipContinuationAsync(int continuationSessionId, string requestingUserId)
    {
        var classSession = await LoadContinuationForSkipAsync(continuationSessionId, requestingUserId);

        if (classSession.Status != Scheduled)
            throw new ClassSessionException(
                ClassSessionErrorCodes.InvalidClassSessionStatus,
                "Buổi phụ đã diễn ra hoặc đã bị huỷ, không thể bỏ được nữa",
                400);

        var studentUserId = classSession.Booking?.Student?.Linkeduserid;
        var isTutor = requestingUserId == classSession.Tutorid;
        var now = TimeZoneHelper.UtcNow;

        if (isTutor)
            classSession.Tutorskipconfirmedat ??= now;
        else
            classSession.Studentskipconfirmedat ??= now;

        await _context.SaveChangesAsync();

        return ToSkipResponse(classSession);
    }

    private async Task<ClassSession> LoadContinuationForSkipAsync(int continuationSessionId, string requestingUserId)
    {
        var classSession = await _context.ClassSessions
            .Include(l => l.Booking)
                .ThenInclude(b => b!.Student)
            .FirstOrDefaultAsync(l => l.Classsessionid == continuationSessionId)
            ?? throw new ClassSessionException(ClassSessionErrorCodes.ClassSessionNotFound, "Không tìm thấy buổi học", 404);

        if (!classSession.Iscontinuation)
            throw new ClassSessionException(ClassSessionErrorCodes.InvalidClassSessionStatus, "Buổi học này không phải buổi phụ", 400);

        var studentUserId = classSession.Booking?.Student?.Linkeduserid;
        var parentId = classSession.Booking?.Student?.Parentid ?? classSession.Booking?.Parentid;
        var isAuthorized = requestingUserId == classSession.Tutorid
            || (!string.IsNullOrEmpty(studentUserId) && requestingUserId == studentUserId)
            || (!string.IsNullOrEmpty(parentId) && requestingUserId == parentId);
        if (!isAuthorized)
            throw new UnauthorizedAccessException("Bạn không có quyền xem thông tin buổi học này.");

        return classSession;
    }

    private static ClassSessionSkipContinuationResponse ToSkipResponse(ClassSession classSession) => new()
    {
        TutorConfirmed = classSession.Tutorskipconfirmedat.HasValue,
        StudentConfirmed = classSession.Studentskipconfirmedat.HasValue,
        BothConfirmed = classSession.Tutorskipconfirmedat.HasValue && classSession.Studentskipconfirmedat.HasValue
    };

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
