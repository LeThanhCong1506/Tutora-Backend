using Hangfire;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.Helpers;
using MV.ApplicationLayer.ServiceInterfaces;
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
    /// <summary>
    /// Thời gian phụ huynh/học sinh có để xem báo cáo và xác nhận buổi học, tính từ lúc gia sư gửi
    /// báo cáo. Hết hạn thì AutoConfirmClassSessionJob tự xác nhận và giải ngân cho gia sư.
    ///
    /// Dùng HẰNG SỐ chứ không viết số trực tiếp vào hai chỗ: trước đây mốc hạn là 12h nhưng câu
    /// thông báo gửi cho phụ huynh lại ghi "trong vòng 24h" — phụ huynh tin theo 24h thì mất hẳn cửa
    /// sổ xem lại, buổi học tự xác nhận và tiền đi luôn ở giờ thứ 12.
    /// </summary>
    private const int ConfirmWindowHours = 12;

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
        // Buổi PHỤ (Iscontinuation) là ngoại lệ: Scheduledend của nó chỉ là mốc ước tính lúc tạo
        // (now + 1h, xem BuildContinuationSession), không phải cam kết giờ học thật — hai bên có
        // thể vào học nốt bất cứ lúc nào trong ngày, không nên bị khoá phòng chỉ vì trễ quá mốc
        // ước tính này. Buổi phụ chỉ thật sự "chết" khi bị huỷ (SubmitReportAsync tự huỷ nếu buổi
        // gốc đã nộp báo cáo mà buổi phụ chưa học) — không dựa vào giờ hẹn.
        if (classSession.Status == Scheduled
            && !classSession.Iscontinuation
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
        else if (classSession.Status == Scheduled && tutorPresent && !studentPresent && !roomClosed
                 && classSession.Istutorpresent != true)
        {
            // Chỉ gia sư có mặt: ghi nhận NGAY (không đợi cả hai) để làm bằng chứng cho việc gửi
            // báo cáo "học viên vắng mặt" sau này (SubmitReportAsync) — không đổi Status, buổi vẫn
            // Scheduled bình thường, chỉ persist đúng 1 lần khi lần đầu phát hiện tutor solo.
            await _context.ClassSessions
                .Where(l => l.Classsessionid == classSessionId && l.Status == Scheduled)
                .ExecuteUpdateAsync(s => s.SetProperty(l => l.Istutorpresent, true));
            classSession.Istutorpresent = true;
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
        await using var tx = await _context.Database.BeginTransactionAsync();
        try
        {
            // Lock trước khi đọc Status: SubmitReportAsync và RequestInterruptionAsync đều có thể
            // đang chuyển Status của CÙNG session này cùng lúc (ClassSession không có concurrency
            // token) — không lock sẽ để bên ghi sau âm thầm đè lên claim của bên trước (VD: gia sư
            // vừa checkout xong nhưng bị RequestInterruptionAsync lật ngược lại thành Interrupted).
            var classSession = await ClassSessionLockHelper.LockById(_context, classSessionId)
                .Include(l => l.Booking)
                .SingleOrDefaultAsync()
                ?? throw new ClassSessionException(ClassSessionErrorCodes.ClassSessionNotFound, "Không tìm thấy buổi học", 404);

            if (classSession.Tutorid != tutorId)
                throw new ClassSessionException(ClassSessionErrorCodes.ClassSessionNotFound, "Không tìm thấy buổi học", 404);

            if (classSession.Status != InProgress)
                throw new ClassSessionException(ClassSessionErrorCodes.InvalidClassSessionStatus, "Buổi học không ở trạng thái đang diễn ra", 400);

            if (!classSession.Checkintime.HasValue)
                throw new ClassSessionException(ClassSessionErrorCodes.NotCheckedIn, "Vui lòng điểm danh vào trước", 400);

            classSession.Checkouttime = TimeZoneHelper.UtcNow;
            classSession.Realend = TimeZoneHelper.UtcNow;

            // ── Tự động dừng Cloud Recording (nếu đang record) ──
            await TryStopRecordingAsync(classSession);

            await _context.SaveChangesAsync();
            await tx.CommitAsync();
            _logger.LogInformation("Tutor {TutorId} checked out from classSession {ClassSessionId}", tutorId, classSessionId);

            return (await GetTutorClassSessionDetailAsync(classSessionId, tutorId))!;
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
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

        var candidates = await _context.ClassSessions
            .Where(l => l.Status == InProgress && l.Checkouttime == null)
            .ToListAsync(ct);

        // Buổi PHỤ (Iscontinuation): Scheduledend chỉ là mốc ước tính lúc TẠO (now+1h, xem
        // BuildContinuationSession) — hai bên có thể vào học nốt trễ hàng giờ so với mốc này, nên
        // "quá giờ" phải tính từ lúc CHECK-IN thật (+ đúng thời lượng đã tính), không phải từ
        // Scheduledend cố định — nếu không, buổi vừa check-in xong sẽ bị job này đóng ngay ở lượt
        // chạy kế tiếp vì Scheduledend đã nằm trong quá khứ từ trước khi ai kịp vào học.
        var expiredSessions = candidates.Where(l =>
        {
            if (l.Iscontinuation && l.Checkintime.HasValue)
            {
                var expectedEnd = l.Checkintime.Value.Add(l.Scheduledend - l.Scheduledstart);
                return expectedEnd <= cutoff;
            }
            return l.Scheduledend <= cutoff;
        }).ToList();

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
    /// Số giờ chờ thêm SAU KHI phòng đã bị <see cref="AutoCloseExpiredLiveSessionsAsync"/> ép đóng
    /// (Checkouttime đã có giá trị) mà gia sư vẫn không nộp báo cáo. Qua mốc này, hệ thống tự nộp 1
    /// báo cáo rỗng thay gia sư — không có bất kỳ job/luồng nào khác từng đưa được buổi ra khỏi
    /// InProgress nếu SubmitReportAsync/RequestInterruptionAsync không được chính người dùng gọi
    /// (kể cả Dispute cũng không nhận session đang InProgress, xem DisputeSettlementPolicy), nên nếu
    /// gia sư đóng trình duyệt luôn sau khi bị ép đóng phòng, session/Booking.Sessionsremaining/escrow
    /// sẽ kẹt vĩnh viễn nếu không có bước này. Phụ huynh vẫn có đủ 12h xác nhận bình thường
    /// (Confirmdeadline, giống mọi báo cáo khác) để tranh chấp nếu thấy buổi không hợp lý.
    /// </summary>
    public const int AutoSubmitMissingReportAfterHours = 6;

    /// <summary>
    /// Tự nộp 1 báo cáo hệ thống cho các buổi <c>in_progress</c> đã bị ép đóng phòng quá
    /// <see cref="AutoSubmitMissingReportAfterHours"/> giờ mà gia sư chưa từng nộp báo cáo thật —
    /// dùng bởi background job (chạy cùng nhịp với AutoCloseExpiredLiveSessionsAsync). Chuyển
    /// InProgress → PendingConfirmation với Confirmdeadline = giờ chạy job + 12h, đi đúng pipeline
    /// xác nhận/tự-động-thanh-toán sẵn có (AutoConfirmClassSessionJob) như mọi báo cáo bình thường.
    /// Thông báo cho cả gia sư lẫn phụ huynh (giống báo cáo thật, xem SubmitReportAsync) — phụ huynh
    /// cần biết để vào xác nhận/tranh chấp trong 12h, không thì không ai hay có báo cáo mới đang chờ.
    /// Trả về số buổi đã tự nộp thay.
    /// </summary>
    public async Task<int> AutoSubmitMissingReportsAsync(CancellationToken ct = default)
    {
        var now = TimeZoneHelper.UtcNow;
        var cutoff = now.AddHours(-AutoSubmitMissingReportAfterHours);

        var stuckSessions = await _context.ClassSessions
            .Include(l => l.Booking)
                .ThenInclude(b => b!.Student)
            .Where(l => l.Status == InProgress
                && l.Checkouttime != null
                && l.Checkouttime <= cutoff
                && !_context.ClassSessionReports.Any(r => r.Classsessionid == l.Classsessionid))
            .ToListAsync(ct);

        if (stuckSessions.Count == 0) return 0;

        var autoSubmittedCount = 0;
        foreach (var classSession in stuckSessions)
        {
            try
            {
                classSession.Status = PendingConfirmation;
                classSession.Submittedat = now;
                classSession.Confirmdeadline = now.AddHours(12);

                _context.ClassSessionReports.Add(new ClassSessionReport
                {
                    Classsessionid = classSession.Classsessionid,
                    Createdbytutorid = classSession.Tutorid,
                    Contentcovered = "[Hệ thống tự nộp] Không nhận được báo cáo từ gia sư sau khi phòng đã tự đóng.",
                    Createdat = now
                });

                await _context.SaveChangesAsync(ct);
                autoSubmittedCount++;

                _logger.LogWarning(
                    "Tự động nộp báo cáo thay cho buổi học {ClassSessionId}: gia sư không nộp báo cáo sau {Hours}h kể từ khi phòng tự đóng.",
                    classSession.Classsessionid, AutoSubmitMissingReportAfterHours);

                if (!string.IsNullOrEmpty(classSession.Tutorid))
                {
                    try
                    {
                        await _notificationService.CreateNotificationAsync(new NotificationRequest
                        {
                            Userid = classSession.Tutorid,
                            Title = "Buổi học đã tự động được chốt",
                            Message = $"Buổi học #{classSession.Classsessionid} đã bị hệ thống tự động chốt do bạn không nộp báo cáo. Vui lòng liên hệ hỗ trợ nếu có sai sót.",
                            Type = NotificationType.LessonReport,
                            Referenceid = classSession.Classsessionid.ToString()
                        });
                    }
                    catch (Exception notifyEx)
                    {
                        _logger.LogWarning(notifyEx, "Không thể gửi thông báo tự nộp báo cáo cho buổi học {ClassSessionId}", classSession.Classsessionid);
                    }
                }

                // Báo cáo hệ thống tự nộp vẫn đi vào đúng pipeline Confirmdeadline=12h như báo cáo
                // thật (xem SubmitReportAsync) — phụ huynh phải được biết để vào xác nhận/tranh chấp,
                // nếu không sẽ không ai hay có báo cáo mới đang chờ.
                var parentId = classSession.Booking?.Student?.Parentid;
                if (!string.IsNullOrEmpty(parentId))
                {
                    try
                    {
                        await _notificationService.CreateNotificationAsync(new NotificationRequest
                        {
                            Userid = parentId,
                            Title = "Buổi học đã tự động được chốt",
                            Message = $"Buổi học #{classSession.Classsessionid} đã bị hệ thống tự động chốt do gia sư không nộp báo cáo. Vui lòng kiểm tra và xác nhận trong vòng 12h.",
                            Type = NotificationType.LessonReport,
                            Referenceid = classSession.Classsessionid.ToString()
                        });
                    }
                    catch (Exception notifyEx)
                    {
                        _logger.LogWarning(notifyEx, "Không thể gửi thông báo tự nộp báo cáo cho phụ huynh của buổi học {ClassSessionId}", classSession.Classsessionid);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Không thể tự nộp báo cáo thay cho buổi học {ClassSessionId}.", classSession.Classsessionid);
            }
        }

        return autoSubmittedCount;
    }

    /// <summary>
    /// Buổi học bị ngắt giữa chừng chỉ được phép có buổi phụ trong đúng ngày bị ngắt (xem
    /// <see cref="RequestInterruptionAsync"/> và guard tương ứng trong
    /// ClassSessionRescheduleProposalService.ProposeAsync). Nếu qua nửa đêm của ngày đó
    /// (Interruptedat.Date + 1 ngày, UTC thuần) mà vẫn chưa xử lý xong, tự đóng buổi gốc theo 1
    /// trong 3 nhánh, tuỳ buổi phụ đã đi tới đâu (buổi phụ có thể đi hết vòng đời bình thường của
    /// riêng nó — check-in → report → confirm/auto-confirm → settle — nếu được học thật mà không skip):
    /// - Buổi phụ CHƯA từng có ai vào (null hoặc Scheduled): buổi gốc được settle qua đường bỏ-qua-
    ///   status-guard mà dispute đang dùng (SettleDisputedClassSessionAsync) nên chuyển thẳng sang
    ///   Completed và trừ Sessionsremaining đúng 1 lần dù đang ở Interrupted (trạng thái mà
    ///   SettleClassSessionAsync bình thường sẽ từ chối); buổi phụ Scheduled chuyển sang Cancelled
    ///   luôn — qua mốc nửa đêm này thì chắc chắn không còn cơ hội nộp báo cáo cho nó nữa nên huỷ
    ///   luôn là an toàn, không mất dữ liệu gì (chưa ai từng thực học nên không có gì để mất).
    /// - Buổi phụ đã check-in thật (InProgress — 2 bên đã vào phòng, có nỗ lực học thật) nhưng không
    ///   ai nộp báo cáo trước hạn: KHÔNG được coi như nhánh trên, vì tự settle buổi gốc "đã dạy đủ
    ///   100%" trong khi phần đã dạy dở ở buổi phụ bị âm thầm huỷ trắng là bất công — cùng gốc bug đã
    ///   xảy ra ở booking #313 (dev) qua cửa SubmitReportAsync (xem
    ///   <see cref="MV.DomainLayer.Exceptions.ClassSessionErrorCodes.ContinuationInProgress"/>). Đóng
    ///   phòng + huỷ buổi phụ cho gọn, nhưng buổi gốc chuyển sang Disputed và hệ thống tự tạo 1
    ///   Dispute (Other, Pending) để admin xem lại và tự chọn mức hoàn/giải ngân qua đúng luồng
    ///   ResolveDisputeAsync sẵn có — hệ thống không tự phán quyết tiền ở bước này, giống hệt cách
    ///   <see cref="AbandonedSessionService.FlagForAdminAsync"/> xử lý ca "1 bên có mặt, 1 bên vắng".
    /// - Buổi phụ ĐÃ có báo cáo/tiền thật gắn vào (PendingConfirmation = đang chờ xác nhận,
    ///   Completed/Issettled = đã tự trừ Sessionsremaining và tự tính vào deliveredCount của
    ///   ReleaseEscrowIfBookingCompleteAsync rồi, HOẶC Disputed = đã có khiếu nại đang chờ admin xử lý
    ///   — muốn khiếu nại được thì bản thân buổi phụ phải từng qua PendingConfirmation/Completed rồi,
    ///   nên đây CŨNG là "đã có báo cáo thật" chứ không phải "chưa dùng"): buổi gốc KHÔNG được settle
    ///   lại và buổi phụ KHÔNG bị đụng vào (khiếu nại nếu có sẽ do admin tự xử lý độc lập qua
    ///   CloseDisputeAsync/ResolveDisputeAsync) — nếu không cùng 1 buổi học logic sẽ bị tính "đã dạy"
    ///   2 lần (gốc bị force-settle Completed trong khi buổi phụ mới là bản ghi thật), hoặc
    ///   report/settle/khiếu nại đang dở dang của buổi phụ bị phá. Buổi gốc chỉ chuyển sang Cancelled
    ///   — buổi phụ mới là bản ghi được settle cho buổi học này, bất kể nó đang ở giai đoạn nào.
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
                var continuation = await _context.ClassSessions.FirstOrDefaultAsync(c =>
                    c.Originalsessionid == original.Classsessionid && c.Iscontinuation, ct);

                // Buổi phụ đã có báo cáo/tiền thật gắn vào rồi (đang chờ xác nhận, đã xong, HOẶC đang
                // bị khiếu nại — Disputed chỉ đạt được từ PendingConfirmation/Completed nên cũng tính
                // là "đã có báo cáo thật", xem docstring ở trên) — không settle lại buổi gốc, và
                // không đụng vào buổi phụ (để admin tự xử lý khiếu nại độc lập nếu có).
                var continuationHasRealRecord = continuation != null
                    && (continuation.Status == PendingConfirmation
                        || continuation.Status == Completed
                        || continuation.Status == Disputed
                        || continuation.Issettled == true);
                var continuationIsDisputed = continuation is { Status: Disputed };
                var continuationWasInProgress = continuation is { Status: InProgress };

                int? autoDisputeId = null;

                if (continuationHasRealRecord)
                {
                    // Buổi phụ đã là bản ghi (được settle, đang chờ settle, hoặc đang chờ admin xử lý
                    // khiếu nại) cho buổi học này — không settle lại buổi gốc, chỉ đóng cho gọn.
                    original.Status = Cancelled;
                    await _context.SaveChangesAsync(ct);
                }
                else if (continuationWasInProgress)
                {
                    // Đã có nỗ lực học thật ở buổi phụ nhưng không ai nộp báo cáo trước hạn — đóng
                    // phòng cho gọn rồi để admin quyết định qua Dispute, không tự cấp full giá.
                    continuation!.Checkouttime ??= now;
                    continuation.Realend ??= now;
                    await TryStopRecordingAsync(continuation);
                    continuation.Status = Cancelled;

                    original.Status = Disputed;
                    var dispute = new Dispute
                    {
                        Classsessionid = original.Classsessionid,
                        Bookingid = original.Bookingid,
                        Createdby = SystemActors.System,
                        Disputetype = DisputeTypes.Other,
                        Reason = $"[Hệ thống tự phát hiện] Buổi học #{original.Classsessionid} bị ngắt giữa chừng, buổi phụ #{continuation.Classsessionid} đã có người vào phòng nhưng không bên nào nộp báo cáo trước khi hết hạn xử lý trong ngày. Cần admin xem lại và quyết định mức hoàn/giải ngân.",
                        Status = DisputeStatus.Pending,
                        Createdat = now
                    };
                    _context.Disputes.Add(dispute);
                    await _context.SaveChangesAsync(ct);
                    autoDisputeId = dispute.Disputeid;

                    try
                    {
                        _backgroundJobClient.Enqueue<IDisputeService>(
                            s => s.ClassifyDisputePriorityAsync(dispute.Disputeid, SystemActors.System, true));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to enqueue priority classification job for auto-detected interruption dispute {DisputeId}", dispute.Disputeid);
                    }
                }
                else
                {
                    // SettleDisputedClassSessionAsync tự mở transaction Serializable riêng của nó —
                    // không bọc thêm transaction ở đây để tránh nested-transaction.
                    await _settlementService.SettleDisputedClassSessionAsync(original.Classsessionid);

                    // Buổi phụ chưa từng có ai vào (null hoặc Scheduled) — qua mốc nửa đêm này chắc
                    // chắn không còn cơ hội nộp báo cáo cho nó nữa, huỷ luôn để không treo lơ lửng
                    // vĩnh viễn trên dashboard.
                    if (continuation is { Status: Scheduled })
                    {
                        continuation.Status = Cancelled;
                        await _context.SaveChangesAsync(ct);
                    }
                }

                closedCount++;

                try
                {
                    var studentUserId = original.Booking?.Student?.Linkeduserid;
                    var parentId = original.Booking?.Student?.Parentid ?? original.Booking?.Parentid;
                    var tutorAndStudentMessage = continuationIsDisputed
                        ? $"Buổi học #{original.Classsessionid} bị ngắt giữa chừng — buổi phụ #{continuation!.Classsessionid} đang có khiếu nại chờ admin xử lý. Hệ thống đã tự động đóng buổi học gốc, kết quả cuối cùng sẽ theo buổi phụ."
                        : continuationHasRealRecord
                            ? $"Buổi học #{original.Classsessionid} bị ngắt giữa chừng đã được hoàn tất thông qua buổi học bù. Hệ thống đã tự động đóng buổi học gốc."
                            : continuationWasInProgress
                                ? $"Buổi học #{original.Classsessionid} bị ngắt giữa chừng và buổi phụ đã quá hạn nộp báo cáo. Hệ thống đã tạo tranh chấp #{autoDisputeId} để admin xem lại — không có gì được tự động chốt."
                                : $"Buổi học #{original.Classsessionid} bị ngắt giữa chừng và đã quá thời hạn học tiếp trong ngày. Hệ thống đã tự động ghi nhận hoàn tất buổi học.";
                    var parentMessage = continuationIsDisputed
                        ? $"Buổi học #{original.Classsessionid} của con bạn bị ngắt giữa chừng — buổi phụ #{continuation!.Classsessionid} đang có khiếu nại chờ admin xử lý. Hệ thống đã tự động đóng buổi học gốc, kết quả cuối cùng sẽ theo buổi phụ."
                        : continuationHasRealRecord
                            ? $"Buổi học #{original.Classsessionid} của con bạn bị ngắt giữa chừng đã được hoàn tất thông qua buổi học bù. Hệ thống đã tự động đóng buổi học gốc."
                            : continuationWasInProgress
                                ? $"Buổi học #{original.Classsessionid} của con bạn bị ngắt giữa chừng và buổi phụ đã quá hạn nộp báo cáo. Hệ thống đã tạo tranh chấp #{autoDisputeId} để admin xem lại — không có gì được tự động chốt."
                                : $"Buổi học #{original.Classsessionid} của con bạn bị ngắt giữa chừng và đã quá thời hạn học tiếp trong ngày. Hệ thống đã tự động ghi nhận hoàn tất buổi học.";
                    var notificationType = continuationWasInProgress || continuationIsDisputed
                        ? NotificationType.DisputeResolved
                        : NotificationType.LessonInterruptionAutoClosed;
                    var notificationTitle = continuationIsDisputed
                        ? "Buổi học bị ngắt đang có khiếu nại chờ xử lý"
                        : continuationWasInProgress
                            ? "Buổi học bị ngắt đang chờ admin xem lại"
                            : "Buổi học bị ngắt đã tự động hoàn tất";
                    // FE route "/disputes/:classSessionId" tra khiếu nại theo ĐÚNG id của session bị
                    // khiếu nại (Dispute.Classsessionid) — case continuationIsDisputed thì khiếu nại
                    // nằm trên buổi PHỤ (continuation), không phải buổi gốc, nên referenceid phải trỏ
                    // đúng continuation.Classsessionid, không thì bấm vào thông báo sẽ ra trang trống
                    // (tra nhầm dispute của buổi gốc, vốn không tồn tại).
                    var notificationReferenceId = continuationIsDisputed
                        ? continuation!.Classsessionid.ToString()
                        : original.Classsessionid.ToString();
                    var notifications = new List<NotificationRequest>();
                    if (!string.IsNullOrWhiteSpace(original.Tutorid))
                        notifications.Add(new NotificationRequest
                        {
                            Userid = original.Tutorid,
                            Title = notificationTitle,
                            Message = tutorAndStudentMessage,
                            Type = notificationType,
                            Referenceid = notificationReferenceId
                        });
                    if (!string.IsNullOrWhiteSpace(studentUserId))
                        notifications.Add(new NotificationRequest
                        {
                            Userid = studentUserId,
                            Title = notificationTitle,
                            Message = tutorAndStudentMessage,
                            Type = notificationType,
                            Referenceid = notificationReferenceId
                        });
                    if (!string.IsNullOrWhiteSpace(parentId))
                        notifications.Add(new NotificationRequest
                        {
                            Userid = parentId,
                            Title = notificationTitle,
                            Message = parentMessage,
                            Type = notificationType,
                            Referenceid = notificationReferenceId
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
    /// True nếu gia sư <paramref name="tutorId"/> đã có 1 buổi học khác (không tính
    /// <paramref name="excludeSessionId"/>, không tính buổi đã Cancelled/CancelledNoshow) chồng giờ
    /// với khung [<paramref name="start"/>, <paramref name="end"/>) — dùng chung cho buổi bù no-show
    /// (<see cref="CreateMakeupClassSessionAsync"/>) và buổi phụ do ngắt kết nối
    /// (<see cref="RequestInterruptionAsync"/>), cả 2 đều tự sinh giờ học mới mà không hỏi lại gia
    /// sư trước, nên phải tự chống double-book thay vì để người dùng tự phát hiện sau.
    /// </summary>
    private async Task<bool> HasTutorSchedulingConflictAsync(string? tutorId, int excludeSessionId, DateTime start, DateTime end)
    {
        if (string.IsNullOrEmpty(tutorId)) return false;

        return await _context.ClassSessions.AnyAsync(l =>
            l.Tutorid == tutorId
            && l.Classsessionid != excludeSessionId
            && l.Status != Cancelled
            && l.Status != CancelledNoshow
            && l.Scheduledstart < end
            && l.Scheduledend > start);
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

            // Recorder audio-only chạy song song, uid riêng — lỗi ở đây KHÔNG được ảnh hưởng recorder
            // video (đã start thành công ở trên): tách try/catch riêng, chỉ log cảnh báo. Thiếu bản
            // audio-only chỉ khiến pipeline AI rơi về nhánh cũ (tải video + ffmpeg), không mất gì cả.
            if (_cloudRecording.AudioOnlyEnabled)
            {
                try
                {
                    var audioHandle = await _cloudRecording.StartAudioAsync(classSession.Classsessionid, channel);
                    classSession.Audiorecordingresourceid = audioHandle.ResourceId;
                    classSession.Audiorecordingsid = audioHandle.Sid;
                    await _context.SaveChangesAsync();
                }
                catch (Exception audioEx)
                {
                    _logger.LogWarning(audioEx,
                        "Không thể bắt đầu recorder audio-only cho buổi học {ClassSessionId} — recorder video vẫn chạy bình thường.",
                        classSession.Classsessionid);
                }
            }
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

        // Dừng recorder audio-only (nếu đã start) — tách hẳn try/catch, lỗi ở đây không phải mất bản ghi
        // thật sự (video mix ở trên vẫn còn), chỉ khiến pipeline AI rơi về nhánh cũ (tải video + ffmpeg).
        if (!string.IsNullOrEmpty(classSession.Audiorecordingresourceid) && !string.IsNullOrEmpty(classSession.Audiorecordingsid))
        {
            try
            {
                var channel = AgoraChannelName.ForSession(classSession.Classsessionid, classSession.Bookingid);
                var audioResult = await _cloudRecording.StopAudioAsync(
                    classSession.Classsessionid, channel, classSession.Audiorecordingresourceid, classSession.Audiorecordingsid);

                var audioKey = audioResult.FileNames.FirstOrDefault(f => f.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
                    ?? audioResult.FileNames.FirstOrDefault(f => f.EndsWith(".m4a", StringComparison.OrdinalIgnoreCase))
                    ?? audioResult.FileNames.FirstOrDefault();

                classSession.Audiorecordings3key = audioKey; // RecordingRelayService forward thẳng lên Gemini rồi xoá
                await _context.SaveChangesAsync();
            }
            catch (Exception audioEx)
            {
                _logger.LogWarning(audioEx,
                    "Không thể dừng recorder audio-only cho buổi học {ClassSessionId} — không ảnh hưởng video mix.",
                    classSession.Classsessionid);
            }
        }
    }

    public async Task<ClassSessionDetailResponse> SubmitReportAsync(int classSessionId, string tutorId, SubmitReportRequest request)
    {
        await using var tx = await _context.Database.BeginTransactionAsync();
        try
        {
            // Lock trước khi đọc Status: nhánh "chỉ tutor có mặt" bên dưới và
            // ReportTutorNoShowAsync (PHHS báo tutor không tới) đều có thể chuyển Status từ
            // Scheduled cho CÙNG 1 session — không lock sẽ để bên ghi sau âm thầm đè lên claim
            // của bên trước (ClassSession không có concurrency token).
            var classSession = await ClassSessionLockHelper.LockById(_context, classSessionId)
                .Include(l => l.Booking)
                    .ThenInclude(b => b!.Student)
                .Include(l => l.ClassSessionReport)
                .SingleOrDefaultAsync()
                ?? throw new ClassSessionException(ClassSessionErrorCodes.ClassSessionNotFound, "Không tìm thấy buổi học", 404);

            if (classSession.Tutorid != tutorId)
                throw new ClassSessionException(ClassSessionErrorCodes.ClassSessionNotFound, "Không tìm thấy buổi học", 404);

            var now = TimeZoneHelper.UtcNow;

            // Nhánh bình thường: buổi đã check-in (cả hai có mặt) → InProgress. Check-in nay là auto
            // khi cả gia sư và học viên cùng vào phòng, nên điều kiện này đảm bảo chỉ những buổi thật
            // sự diễn ra (cả 2 có mặt) mới gửi được báo cáo → mới đi tiếp tới thanh toán.
            //
            // Ngoại lệ 1: chỉ gia sư có mặt (đã ghi Istutorpresent=true ở TryAutoCheckInAsync khi
            // học viên không vào phòng) và đã qua giờ kết thúc dự kiến — coi như học viên vắng mặt,
            // cho gia sư tự báo cáo để đi tiếp vào đúng pipeline xác nhận/tự-động-thanh-toán 12h sẵn
            // có (PHHS vẫn có cửa sổ đó để phản đối nếu thấy không hợp lý).
            var isSoloTutorNoShow = classSession.Status == Scheduled
                && classSession.Istutorpresent == true
                && classSession.Isstudentpresent != true
                && now > classSession.Scheduledend;

            // Ngoại lệ 2: buổi bị báo ngắt (status=interrupted) không bao giờ tự quay lại in_progress
            // được nữa (xem RequestInterruptionAsync). Quyết định sản phẩm: cho gửi báo cáo NGAY khi
            // đang interrupted, không cần chờ 2 bên đồng ý bỏ buổi phụ nữa — NHƯNG chỉ khi buổi phụ
            // chưa từng có ai vào (Scheduled); nếu buổi phụ đã check-in thật (InProgress) thì KHÔNG
            // cho chốt buổi gốc kiểu này nữa (xem throw ContinuationInProgress bên dưới) — tránh gia
            // sư "lách" ngắt sớm rồi bỏ qua phần đã dạy dở ở buổi phụ để hưởng full giá buổi gốc. Nếu
            // buổi phụ đang Scheduled chưa dùng, tự huỷ luôn trong cùng transaction để không treo lơ
            // lửng (xem bên dưới).
            //
            // Nhưng nếu buổi phụ đã CÓ báo cáo/tiền thật gắn vào rồi — đang chờ xác nhận
            // (PendingConfirmation) hoặc đã xong (Completed/Issettled, đã tự trừ Sessionsremaining +
            // tự tính vào deliveredCount của ReleaseEscrowIfBookingCompleteAsync) — KHÔNG được cho
            // nộp báo cáo buổi gốc nữa, nếu không cùng 1 buổi học logic sẽ bị tính "đã dạy" 2 lần,
            // hoặc report/settle đang dở dang của buổi phụ bị phá. Cùng gốc bug với
            // AutoCloseExpiredInterruptedSessionsAsync, chỉ khác cửa vào (tutor tự nộp thay vì job nền).
            ClassSession? continuationToCancel = null;
            if (classSession.Status == Interrupted)
            {
                var continuation = await _context.ClassSessions.FirstOrDefaultAsync(
                    c => c.Originalsessionid == classSessionId && c.Iscontinuation);

                if (continuation != null &&
                    (continuation.Status == PendingConfirmation || continuation.Status == Completed || continuation.Issettled == true))
                    throw new ClassSessionException(
                        ClassSessionErrorCodes.ContinuationAlreadySettled,
                        $"Buổi phụ #{continuation.Classsessionid} đã có báo cáo/được xử lý rồi — buổi học này đã được coi là xong, không cần nộp báo cáo nữa.",
                        400);

                // Buổi phụ đã check-in thật (InProgress, 2 bên đã vào phòng và đang dạy dở) — không
                // cho nộp báo cáo buổi gốc để "lách" qua phần đã dạy dở đó và hưởng full giá buổi
                // gốc trong khi buổi phụ chưa hề được ghi nhận. Bắt nộp báo cáo trên chính buổi phụ.
                if (continuation is { Status: InProgress })
                    throw new ClassSessionException(
                        ClassSessionErrorCodes.ContinuationInProgress,
                        $"Buổi phụ #{continuation.Classsessionid} đang diễn ra (đã điểm danh vào) — vui lòng nộp báo cáo trên buổi phụ đó, không nộp trên buổi gốc.",
                        400);

                // Buổi phụ chưa từng có ai vào (Scheduled) — huỷ luôn cùng lúc với việc chốt buổi
                // gốc để không treo lơ lửng vĩnh viễn; không mất dữ liệu gì vì chưa ai thực học.
                if (continuation is { Status: Scheduled })
                    continuationToCancel = continuation;
            }

            // Yêu cầu buổi đã được check-in (in_progress), đang bị ngắt (interrupted), hoặc là
            // ngoại lệ solo-tutor-no-show ở trên.
            if (classSession.Status != InProgress && classSession.Status != Interrupted && !isSoloTutorNoShow)
                throw new ClassSessionException(
                    ClassSessionErrorCodes.InvalidClassSessionStatus,
                    "Buổi học phải đang diễn ra (đã điểm danh vào), đã bị ngắt giữa chừng, hoặc học viên không vào lớp sau giờ kết thúc, mới gửi được báo cáo",
                    400);

            if (classSession.ClassSessionReport != null)
                throw new ClassSessionException(ClassSessionErrorCodes.ReportAlreadySubmitted, "Báo cáo buổi học đã được gửi rồi", 400);

            classSession.Lessoncontent = request.ContentCovered;
            classSession.Homework = request.HomeworkAssigned;
            classSession.Tutornotes = request.TutorNotes;
            // Điểm danh có mặt (Istutorpresent/Isstudentpresent) đã được ghi lúc auto check-in từ
            // presence THẬT — không ghi đè bằng giá trị tự khai trong request. Chỉ nhận ghi chú.
            classSession.Attendancenote = isSoloTutorNoShow
                ? $"[Học viên không vào lớp] {request.AttendanceNote}".Trim()
                : request.AttendanceNote;

            // Buổi đã check-in thật (Checkintime có giá trị) nhưng gia sư nộp báo cáo TRƯỚC khi
            // bấm "Kết thúc buổi học" (CheckOutAsync chưa từng được gọi): phải tự đóng phòng +
            // dừng ghi hình ngay tại đây. Nếu không, Status chuyển PendingConfirmation ngay bên
            // dưới khiến CheckOutAsync (yêu cầu Status==InProgress) vĩnh viễn không gọi được nữa,
            // và Cloud Recording sẽ không còn bất kỳ đường nào để dừng (AutoCloseExpiredLiveSessionsAsync
            // cũng chỉ quét Status==InProgress, không còn khớp session này nữa).
            if (classSession.Checkintime.HasValue && !classSession.Checkouttime.HasValue)
            {
                classSession.Checkouttime = now;
                classSession.Realend = now;
                await TryStopRecordingAsync(classSession);
            }

            classSession.Status = PendingConfirmation;
            classSession.Submittedat = now;
            classSession.Confirmdeadline = now.AddHours(ConfirmWindowHours);

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

            // Luôn còn Scheduled (chưa ai vào phòng) — nhánh InProgress đã bị chặn bằng throw ở trên,
            // nên không cần đóng phòng/dừng ghi hình ở đây.
            if (continuationToCancel != null)
                continuationToCancel.Status = Cancelled;

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
                    Message = $"Gia sư đã gửi báo cáo cho buổi học #{classSessionId}. Vui lòng kiểm tra và xác nhận trong vòng {ConfirmWindowHours}h — quá hạn hệ thống sẽ tự xác nhận.",
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
            // Lock trước khi đọc Status: CheckOutAsync/SubmitReportAsync có thể đang chuyển Status
            // của CÙNG session này cùng lúc (ClassSession không có concurrency token) — không lock
            // sẽ để bên ghi sau âm thầm đè lên claim của bên trước (VD: gia sư vừa checkout xong
            // nhưng bị request này lật ngược lại thành Interrupted, sinh buổi phụ thừa).
            var classSession = await ClassSessionLockHelper.LockById(_context, classSessionId)
                .Include(l => l.Booking)
                    .ThenInclude(b => b!.Student)
                .SingleOrDefaultAsync()
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

            // Buổi bù no-show (Ismakeup) không nằm trong 2 check trên nên vẫn báo ngắt được — nếu
            // không chặn thêm ở đây, 1 buổi bù đứng cuối chuỗi đã chạm MaxRelearnSessionsPerChain vẫn
            // có thể bị báo ngắt để sinh thêm buổi phụ, vượt cap mà CreateMakeupClassSessionAsync
            // (đường "học bù") đang tuân thủ. Cùng giới hạn, cùng cách đếm với đường đó.
            var existingSessionCount = await DisputeRelearnPolicy.CountSessionsInChainAsync(_context, classSessionId);
            if (existingSessionCount >= DisputeRelearnPolicy.MaxRelearnSessionsPerChain)
                throw new ClassSessionException(
                    ClassSessionErrorCodes.SessionChainLimitReached,
                    $"Chuỗi buổi học này đã có {DisputeRelearnPolicy.MaxRelearnSessionsPerChain} buổi — không thể tạo thêm buổi phụ nữa.",
                    409);

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

            // Đã dạy thật bằng/vượt thời lượng đăng ký (phần "còn thiếu" đã về 0) — báo ngắt lúc này
            // chỉ tạo ra 1 buổi phụ 30 phút không phản ánh sự cố thật nào (xem HasRemainingScheduledTime).
            // Buổi đã học đủ giờ thì dùng đường "Kết thúc bình thường", không còn lý do báo ngắt nữa.
            var checkInTimeForRemaining = classSession.Checkintime ?? classSession.Scheduledstart;
            if (!ClassSessionInterruptionPolicy.HasRemainingScheduledTime(
                    classSession.Scheduledstart, classSession.Scheduledend, checkInTimeForRemaining, now))
                throw new ClassSessionException(
                    ClassSessionErrorCodes.InterruptionNoRemainingTime,
                    "Buổi học đã học đủ hoặc vượt thời lượng đăng ký, không còn phần thiếu để tạo buổi phụ — không thể báo ngắt giữa chừng nữa.",
                    400);

            classSession.Status = Interrupted;
            classSession.Interruptedat = now;
            classSession.Interruptreason = reason;
            classSession.Interruptedby = requestingUserId;
            classSession.Checkouttime = now;
            classSession.Realend = now;

            await TryStopRecordingAsync(classSession);

            var continuation = ClassSessionInterruptionPolicy.BuildContinuationSession(classSession, now);

            // Giờ mặc định (now+1h) do HỆ THỐNG tự tính, không phải gia sư chọn — nếu trùng lịch dạy
            // khác của chính gia sư này, tự dời từng 30 phút (tối đa 3 tiếng, tính từ giờ mặc định
            // now+1h — tổng cộng thử trong khung 4h kể từ bây giờ) thay vì chặn cứng request ngắt
            // buổi (người dùng không hề nhập giờ này để mà sửa lại). Nếu vẫn không tìm được khe
            // trống, giữ nguyên giờ mặc định cuối cùng, chỉ log cảnh báo, VÀ báo rõ cho gia sư biết
            // (xem notification LessonContinuationScheduleConflict bên dưới) — 2 bên vẫn có thể tự
            // dùng luồng "Đề xuất đổi lịch" sẵn có để thương lượng lại (Status vẫn Scheduled nên
            // tương thích), chỉ là hệ thống không tự đoán được khe trống nào hợp lý hơn nữa.
            const int maxShiftAttempts = 6;
            var shift = TimeSpan.FromMinutes(30);
            var scheduleConflictRemains = false;
            for (var attempt = 0; attempt < maxShiftAttempts; attempt++)
            {
                var hasConflict = await HasTutorSchedulingConflictAsync(
                    classSession.Tutorid, classSessionId, continuation.Scheduledstart, continuation.Scheduledend);
                if (!hasConflict)
                {
                    scheduleConflictRemains = false;
                    break;
                }

                scheduleConflictRemains = true;
                continuation.Scheduledstart = continuation.Scheduledstart.Add(shift);
                continuation.Scheduledend = continuation.Scheduledend.Add(shift);

                if (attempt == maxShiftAttempts - 1)
                    _logger.LogWarning(
                        "Buổi phụ cho classSession {ClassSessionId} vẫn trùng lịch gia sư sau {Attempts} lần tự dời giờ — giữ nguyên giờ {Start:o}, cần 2 bên tự đề xuất đổi lịch.",
                        classSessionId, maxShiftAttempts, continuation.Scheduledstart);
            }

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

            // Hệ thống đã thử tự tìm giờ trong 1h mặc định + 3h tự dời mà vẫn không ra khe trống —
            // phải báo rõ với gia sư lý do (đang có lịch dạy trùng), không thì gia sư không biết
            // buổi phụ đang được tạo ở giờ nào để mà tự đề xuất đổi lịch.
            if (scheduleConflictRemains && !string.IsNullOrWhiteSpace(classSession.Tutorid))
            {
                try
                {
                    await _notificationService.CreateNotificationAsync(new NotificationRequest
                    {
                        Userid = classSession.Tutorid,
                        Title = "Chưa thể sắp xếp buổi học phụ",
                        Message = $"Hệ thống chưa thể tự sắp xếp giờ học phù hợp cho buổi học phụ #{continuation.Classsessionid} (của buổi #{classSessionId} bị ngắt giữa chừng) vì bạn đã có lịch dạy khác trùng khung giờ đó. Vui lòng chủ động đề xuất đổi lịch cho buổi học phụ này.",
                        Type = NotificationType.LessonContinuationScheduleConflict,
                        Referenceid = continuation.Classsessionid.ToString()
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Không thể gửi thông báo xung đột lịch buổi phụ cho classSession {ClassSessionId}", classSessionId);
                }
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
                CanEverBeInterrupted = canEverBeInterrupted,
                HasRemainingTime = true
            };
        }

        var isFirstSessionOfBooking = await ClassSessionInterruptionPolicy.IsFirstOriginalSessionAsync(_context, classSession);
        var sessionLog = await _sessionLogService.GetSessionLogAsync(classSessionId);
        var overlapRatio = sessionLog?.Summary.OverlapRatio ?? 0.0;
        var requiredRatio = ClassSessionInterruptionPolicy.ThresholdFor(isFirstSessionOfBooking);
        var checkInTimeForRemaining = classSession.Checkintime ?? classSession.Scheduledstart;
        var hasRemainingTime = ClassSessionInterruptionPolicy.HasRemainingScheduledTime(
            classSession.Scheduledstart, classSession.Scheduledend, checkInTimeForRemaining, TimeZoneHelper.UtcNow);

        return new ClassSessionInterruptionEligibilityResponse
        {
            Eligible = ClassSessionInterruptionPolicy.MeetsThreshold(isFirstSessionOfBooking, overlapRatio) && hasRemainingTime,
            CurrentRatio = overlapRatio,
            RequiredRatio = requiredRatio,
            HasRemainingTime = hasRemainingTime
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

        // Xác nhận này vừa làm cả 2 bên đồng ý xong — mọi đề xuất đổi lịch còn treo cho đúng buổi
        // phụ này giờ vô nghĩa (2 bên đã thống nhất bỏ hẳn, không phải dời giờ), tự hết hạn luôn
        // để UI không còn hiện nút Xác nhận/Từ chối chết (xem guard tương ứng ở
        // ClassSessionRescheduleProposalService.ProposeAsync/RespondAsync).
        if (classSession.Tutorskipconfirmedat.HasValue && classSession.Studentskipconfirmedat.HasValue)
        {
            var pendingProposal = await _context.ClassSessionRescheduleProposals.FirstOrDefaultAsync(
                x => x.Classsessionid == continuationSessionId && x.Status == RescheduleProposalStatus.Pending);
            if (pendingProposal != null)
            {
                pendingProposal.Status = RescheduleProposalStatus.Expired;
                pendingProposal.Updatedat = now;
            }
        }

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
