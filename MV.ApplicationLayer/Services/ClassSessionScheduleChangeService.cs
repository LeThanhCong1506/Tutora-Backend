using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.Helpers;
using MV.ApplicationLayer.Interfaces;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Entities;
using MV.DomainLayer.Helpers;

namespace MV.ApplicationLayer.Services;

public class ClassSessionScheduleChangeService(
    IAppDbContext db,
    INotificationService notificationService,
    ILogger<ClassSessionScheduleChangeService> logger) : IClassSessionScheduleChangeService
{
    private const int ConfirmationLifetimeMinutes = 30;


    public async Task<SessionScheduleChangeResponse> GetExistingStateAsync(
        int classSessionId,
        string userId,
        string? role,
        CancellationToken cancellationToken = default)
    {
        var session = await LoadSessionAsync(classSessionId, cancellationToken)
            ?? throw new KeyNotFoundException("Không tìm thấy buổi học.");
        EnsureAccess(session, userId, role);

        var latest = await db.ClassSessionScheduleChanges
            .Where(x => x.Classsessionid == classSessionId)
            .OrderByDescending(x => x.Schedulechangeid)
            .FirstOrDefaultAsync(cancellationToken);
        if (latest == null)
            return Map(session, null, userId, false, session.Status == ClassSessionStatus.InProgress);

        var now = TimeZoneHelper.UtcNow;

        // Read-only path never creates a new request (see ParentController.GetScheduleChange), but it
        // must still surface a request that timed out while nobody was polling it — otherwise Status
        // stays "pending" forever even though RequiresConfirmation has already gone false, and neither
        // side is told the window closed.
        var justExpired = (latest.Status == ScheduleChangeStatus.Pending
                || latest.Status == ScheduleChangeStatus.Approved)
            && latest.Expiresat <= now;
        if (justExpired)
        {
            latest.Status = ScheduleChangeStatus.Expired;
            latest.Updatedat = now;
            await db.SaveChangesAsync(cancellationToken);
            await NotifyExpiredAsync(session, latest);
        }

        var isActive = (latest.Status == ScheduleChangeStatus.Pending
                || latest.Status == ScheduleChangeStatus.Approved)
            && latest.Expiresat > now;
        var isRejectedDuringCooldown = latest.Status == ScheduleChangeStatus.Rejected
            && latest.Rejectedat.HasValue
            && latest.Rejectedat.Value.AddMinutes(ConfirmationLifetimeMinutes) > now;

        // Reuse the authoritative path for an active request. Besides mapping the state,
        // it repairs legacy requests whose learner signer is no longer the correct parent.
        if (isActive || isRejectedDuringCooldown)
            return await GetOrCreateStateAsync(classSessionId, userId, role, cancellationToken);

        return Map(
            session,
            latest,
            userId,
            false,
            latest.Status == ScheduleChangeStatus.Applied
                || session.Status == ClassSessionStatus.InProgress);
    }
    public async Task<SessionScheduleChangeResponse> GetOrCreateStateAsync(
        int classSessionId,
        string userId,
        string? role,
        CancellationToken cancellationToken = default)
    {
        var session = await LoadSessionAsync(classSessionId, cancellationToken)
            ?? throw new KeyNotFoundException("Không tìm thấy buổi học.");
        EnsureAccess(session, userId, role);

        var now = TimeZoneHelper.UtcNow;
        var latest = await db.ClassSessionScheduleChanges
            .Where(x => x.Classsessionid == classSessionId)
            .OrderByDescending(x => x.Schedulechangeid)
            .FirstOrDefaultAsync(cancellationToken);
        var approver = ResolveLearnerApprover(session);

        if (string.Equals(role, UserRole.Admin, StringComparison.OrdinalIgnoreCase)
            || session.Status == ClassSessionStatus.InProgress)
            return Map(session, latest, userId, false, true);

        if (IsWithinNormalWindow(session, now))
        {
            if (latest?.Status == ScheduleChangeStatus.Pending || latest?.Status == ScheduleChangeStatus.Approved)
            {
                latest.Status = ScheduleChangeStatus.Expired;
                latest.Updatedat = now;
                await db.SaveChangesAsync(cancellationToken);
            }
            return Map(session, latest, userId, false, true);
        }

        // Ngoài khung giờ bình thường — nếu đang có đề xuất đổi lịch (tính năng chủ động chọn giờ
        // mới, ClassSessionRescheduleProposal) chờ phản hồi, khoá hẳn cổng xác nhận vào học ngoài
        // giờ: không tạo request off-schedule mới, không cho vào phòng qua đường này. Tránh 2 cơ
        // chế đổi giờ (đề xuất mới + xác nhận ngoài giờ cũ) chạy song song rồi ghi đè lẫn nhau.
        var hasPendingReschedule = await db.ClassSessionRescheduleProposals
            .AnyAsync(x => x.Classsessionid == classSessionId
                && x.Status == RescheduleProposalStatus.Pending
                && x.Expiresat > now, cancellationToken);
        if (hasPendingReschedule)
        {
            var blockedResponse = Map(session, latest, userId, false, false);
            blockedResponse.RescheduleProposalPending = true;
            return blockedResponse;
        }

        // ── Vào lớp ngoài khung giờ đã hẹn KHÔNG còn cần xác nhận đồng thuận ────────────────
        //
        // Luật giờ đồng nhất với trường hợp đúng giờ: đủ hai bên trong lobby là vào được. Trước
        // đây ngoài khung giờ phải qua một vòng xin/duyệt riêng (gia sư gửi yêu cầu, phía học viên
        // bấm đồng ý), khiến cùng một hành động lại có hai luật khác nhau tuỳ vào chênh lệch mấy
        // phút so với giờ hẹn.
        //
        // Những rào chắn KHÁC vẫn giữ nguyên và không bị ảnh hưởng:
        //  - Đề xuất đổi lịch đang chờ (khối ngay phía trên) vẫn khoá cổng vào lớp.
        //  - Trùng lịch với buổi khác vẫn bị chặn ở AgoraController.AdmitAndBuildRoomAsync qua
        //    ClassSessionScheduleConflictGuard — không đi qua đường xác nhận này.
        //  - Phòng vẫn tự đóng sau giờ kết thúc + 30 phút (ClassSessionService.M3.Attendance).
        //
        // Hệ quả: Scheduledstart/Scheduledend KHÔNG còn bị ghi đè khi vào sớm (trước đây yêu cầu
        // được duyệt sẽ dời lịch về giờ hiện tại). Giờ hẹn gốc giữ nguyên như cam kết ban đầu, còn
        // giờ vào học thật được ghi ở Checkintime/Realstart.
        var staleRequest = latest is not null
            && (latest.Status == ScheduleChangeStatus.Pending || latest.Status == ScheduleChangeStatus.Approved);

        if (staleRequest)
        {
            // Dọn yêu cầu còn sót từ luồng cũ để không hiện lại trên giao diện.
            latest!.Status = ScheduleChangeStatus.Expired;
            latest.Updatedat = now;
            await db.SaveChangesAsync(cancellationToken);
        }

        return Map(session, latest, userId, false, true);

#pragma warning disable CS0162 // Luồng xác nhận cũ giữ lại để bật lại được nếu đổi ý.
        if (latest != null
            && (latest.Status == ScheduleChangeStatus.Pending
                || latest.Status == ScheduleChangeStatus.Approved
                || latest.Status == ScheduleChangeStatus.Rejected)
            && (latest.Learnerapproveruserid != approver.UserId
                || !string.Equals(latest.Learnerapproverrole, approver.Role, StringComparison.OrdinalIgnoreCase)))
        {
            // A learner can become parent-managed, or an earlier version may have assigned
            // the student before checking ParentId. Never preserve an active consent request
            // whose required signer no longer matches the current booking owner.
            latest.Status = ScheduleChangeStatus.Expired;
            latest.Updatedat = now;
            await db.SaveChangesAsync(cancellationToken);
        }

        if (latest != null
            && (latest.Status == ScheduleChangeStatus.Pending || latest.Status == ScheduleChangeStatus.Approved)
            && latest.Expiresat <= now)
        {
            latest.Status = ScheduleChangeStatus.Expired;
            latest.Updatedat = now;
            await db.SaveChangesAsync(cancellationToken);
        }

        if (latest?.Status == ScheduleChangeStatus.Approved && latest.Expiresat > now)
        {
            var response = Map(session, latest, userId, true, true);
            var conflict = await FindCurrentConflictAsync(classSessionId, now, cancellationToken);
            response.ScheduleConflict = conflict;
            response.AdmissionAllowed = conflict == null;
            return response;
        }
        if (latest?.Status == ScheduleChangeStatus.Pending && latest.Expiresat > now)
            return Map(session, latest, userId, true, false);
        if (latest?.Status == ScheduleChangeStatus.Rejected
            && latest.Rejectedat.HasValue
            && latest.Rejectedat.Value.AddMinutes(ConfirmationLifetimeMinutes) > now)
            return Map(session, latest, userId, true, false);

        if (string.IsNullOrWhiteSpace(session.Tutorid) || string.IsNullOrWhiteSpace(approver.UserId))
            throw new InvalidOperationException("Buổi học thiếu thông tin người xác nhận.");

        var change = new ClassSessionScheduleChange
        {
            Classsessionid = classSessionId,
            Originalscheduledstart = session.Scheduledstart,
            Originalscheduledend = session.Scheduledend,
            Tutoruserid = session.Tutorid,
            Learnerapproveruserid = approver.UserId,
            Learnerapproverrole = approver.Role,
            Requestedat = now,
            Expiresat = now.AddMinutes(ConfirmationLifetimeMinutes),
            Status = ScheduleChangeStatus.Pending,
            Createdat = now,
            Updatedat = now
        };
        db.ClassSessionScheduleChanges.Add(change);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Tutor and learner can enter the lobby at the same instant. The filtered unique
            // index is authoritative; the losing request reuses the winner instead of failing.
            db.ClassSessionScheduleChanges.Remove(change);
            change = await db.ClassSessionScheduleChanges
                .Where(x => x.Classsessionid == classSessionId
                    && (x.Status == ScheduleChangeStatus.Pending || x.Status == ScheduleChangeStatus.Approved))
                .OrderByDescending(x => x.Schedulechangeid)
                .FirstOrDefaultAsync(cancellationToken)
                ?? throw new InvalidOperationException("Không thể tạo yêu cầu xác nhận đổi lịch.");
        }

        await NotifyApproversAsync(session, change, userId);
        return Map(session, change, userId, true, false);
#pragma warning restore CS0162
    }

    public async Task<SessionScheduleChangeResponse> RespondAsync(
        int classSessionId,
        string userId,
        string? role,
        bool confirmed,
        CancellationToken cancellationToken = default)
    {
        var current = await GetOrCreateStateAsync(classSessionId, userId, role, cancellationToken);
        if (!current.RequiresConfirmation)
            return current;

        var session = await LoadSessionAsync(classSessionId, cancellationToken)
            ?? throw new KeyNotFoundException("Không tìm thấy buổi học.");
        var change = await db.ClassSessionScheduleChanges
            .Where(x => x.Classsessionid == classSessionId
                && (x.Status == ScheduleChangeStatus.Pending || x.Status == ScheduleChangeStatus.Approved))
            .OrderByDescending(x => x.Schedulechangeid)
            .FirstOrDefaultAsync(cancellationToken);

        if (change == null)
            throw new InvalidOperationException("Yêu cầu đổi lịch không còn hiệu lực.");
        if (change.Expiresat <= TimeZoneHelper.UtcNow)
            throw new InvalidOperationException("Yêu cầu đổi lịch đã hết hạn. Vui lòng tải lại phòng chờ.");

        var isTutor = change.Tutoruserid == userId;
        var isLearnerApprover = change.Learnerapproveruserid == userId;
        if (!isTutor && !isLearnerApprover)
            throw new UnauthorizedAccessException("Bạn không phải người có quyền xác nhận thay đổi lịch này.");
        if (change.Status == ScheduleChangeStatus.Approved)
            return Map(session, change, userId, true, true);

        var now = TimeZoneHelper.UtcNow;
        await using var decisionTransaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken)
            : null;
        if (!confirmed)
        {
            change.Status = ScheduleChangeStatus.Rejected;
            change.Rejectedat = now;
            change.Rejectedby = userId;
            change.Updatedat = now;
            await db.SaveChangesAsync(cancellationToken);
            if (decisionTransaction != null)
                await decisionTransaction.CommitAsync(cancellationToken);
            return Map(session, change, userId, true, false);
        }

        if (isTutor && !change.Tutorconfirmedat.HasValue)
        {
            change.Tutorconfirmedat = now;
            change.Tutorconfirmedby = userId;
        }
        if (isLearnerApprover && !change.Learnerconfirmedat.HasValue)
        {
            change.Learnerconfirmedat = now;
            change.Learnerconfirmedby = userId;
        }

        if (change.Tutorconfirmedat.HasValue && change.Learnerconfirmedat.HasValue)
        {
            change.Status = ScheduleChangeStatus.Approved;
            change.Approvedat = now;
        }
        change.Updatedat = now;
        await db.SaveChangesAsync(cancellationToken);

        SessionScheduleConflictResponse? approvalConflict = null;
        if (change.Status == ScheduleChangeStatus.Approved)
        {
            approvalConflict = await FindCurrentConflictAsync(classSessionId, now, cancellationToken);

            // Dời buổi học ngay khi hai bên chốt, thay vì chờ tới lúc điểm danh. Trước đây chỉ
            // đổi trạng thái của yêu cầu nên trang lịch vẫn hiển thị giờ cũ cho tới khi gia sư
            // điểm danh — người dùng đồng ý xong nhìn lịch không thấy gì thay đổi.
            // Không dời khi đang trùng lịch: để nguyên giờ cũ còn hơn đẩy buổi học vào chỗ kẹt.
            if (approvalConflict == null && session.Status == ClassSessionStatus.Scheduled)
            {
                var adjustedStart = now;
                var adjustedEnd = now.Add(change.Originalscheduledend - change.Originalscheduledstart);

                // Nạp bản ghi có tracking rồi gán, không dùng ExecuteUpdateAsync: bộ test chạy
                // trên provider InMemory và provider đó không hỗ trợ ExecuteUpdate.
                var sessionRow = await db.ClassSessions
                    .FirstOrDefaultAsync(
                        x => x.Classsessionid == classSessionId && x.Status == ClassSessionStatus.Scheduled,
                        cancellationToken);

                if (sessionRow != null)
                {
                    sessionRow.Scheduledstart = adjustedStart;
                    sessionRow.Scheduledend = adjustedEnd;

                    change.Adjustedscheduledstart = adjustedStart;
                    change.Adjustedscheduledend = adjustedEnd;
                    change.Updatedat = now;
                    await db.SaveChangesAsync(cancellationToken);

                    // Map() đọc từ snapshot đã nạp trước khi dời, nên phải đồng bộ lại nếu không
                    // response trả về ngay sau đó vẫn mang giờ cũ.
                    session = session with { Scheduledstart = adjustedStart, Scheduledend = adjustedEnd };
                }
            }
        }

        if (decisionTransaction != null)
            await decisionTransaction.CommitAsync(cancellationToken);

        var response = Map(session, change, userId, true, change.Status == ScheduleChangeStatus.Approved);
        response.ScheduleConflict = approvalConflict;
        response.AdmissionAllowed = response.AdmissionAllowed && approvalConflict == null;
        return response;
    }

    private Task<SessionSnapshot?> LoadSessionAsync(int classSessionId, CancellationToken cancellationToken)
        => db.ClassSessions
            .AsNoTracking()
            .Where(x => x.Classsessionid == classSessionId)
            .Select(x => new SessionSnapshot(
                x.Classsessionid,
                x.Status,
                x.Scheduledstart,
                x.Scheduledend,
                x.Tutorid,
                x.Tutor != null && x.Tutor.Tutor != null ? x.Tutor.Tutor.Fullname : null,
                x.Studentid,
                x.Booking != null && x.Booking.Student != null && x.Booking.Student.Parentid != null
                    ? x.Booking.Student.Parentid
                    : x.Booking != null ? x.Booking.Parentid : null,
                x.Booking != null && x.Booking.Student != null && x.Booking.Student.Parent != null
                    ? x.Booking.Student.Parent.Fullname
                    : x.Booking != null && x.Booking.Parent != null ? x.Booking.Parent.Fullname : null,
                x.Booking != null && x.Booking.Student != null ? x.Booking.Student.Linkeduserid : null,
                x.Booking != null && x.Booking.Student != null ? x.Booking.Student.Fullname : null,
                x.Booking != null && x.Booking.Student != null && x.Booking.Student.Linkeduser != null
                    ? x.Booking.Student.Linkeduser.Birthdate
                    : x.Booking != null && x.Booking.Student != null
                        ? x.Booking.Student.Birthdate
                        : null,
                x.Iscontinuation))
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>Vào lớp trước giờ hẹn tối đa bao nhiêu phút vẫn coi là bình thường, không cần xác nhận.</summary>
    private const int EarlyJoinToleranceMinutes = 15;

    // Khôi phục lại cơ chế xác nhận đồng thuận khi vào lớp sớm/ngoài giờ đặt (đã từng bị gỡ theo
    // yêu cầu sản phẩm trước đó, nay khôi phục theo yêu cầu mới — học sinh dưới 16 tuổi cần phụ
    // huynh xác nhận thay, xem ResolveLearnerApprover).
    //
    // Buổi PHỤ (Iscontinuation=true) là ngoại lệ — LUÔN coi là trong khung giờ bình thường, không
    // bao giờ cần xác nhận: Scheduledstart/end của nó chỉ là mốc ước tính lúc tạo (now+1h), không
    // phải giờ hẹn thật, và một khi buổi gốc đã được xác nhận dời lịch thì buổi phụ phải học tiếp
    // được bất cứ lúc nào cho tới hạn tự huỷ nửa đêm hôm sau (xem comment ở
    // AutoCloseExpiredLiveSessionsAsync/ClassSessionService.M3.Attendance.cs) — không nên bị chặn
    // lại bởi đúng cơ chế mà buổi gốc vừa mới vượt qua.
    private static bool IsWithinNormalWindow(SessionSnapshot session, DateTime now)
        => session.Iscontinuation
            || (now >= session.Scheduledstart.AddMinutes(-EarlyJoinToleranceMinutes)
                && now <= session.Scheduledend);

    private static (string UserId, string Role, string Name) ResolveLearnerApprover(SessionSnapshot session)
    {
        // A student profile owned by a parent remains parent-managed. This mirrors the
        // booking authorization rule: the linked student account cannot act for that booking.
        if (!string.IsNullOrWhiteSpace(session.ParentId))
            return (session.ParentId!, UserRole.Parent, session.ParentName ?? "Phụ huynh");

        var studentCanConfirm = AgeHelper.IsOldEnoughToSelfBook(session.StudentBirthdate)
            && !string.IsNullOrWhiteSpace(session.StudentUserId);
        if (studentCanConfirm)
            return (session.StudentUserId!, UserRole.Student, session.StudentName ?? "Học sinh");

        // Invalid legacy data: an underage student without a parent must not be allowed
        // to self-consent merely because a linked login exists.
        return (string.Empty, UserRole.Parent, "Phụ huynh");
    }

    private static void EnsureAccess(SessionSnapshot session, string userId, string? role)
    {
        if (string.Equals(role, UserRole.Admin, StringComparison.OrdinalIgnoreCase)
            || session.Tutorid == userId
            || session.ParentId == userId
            || session.StudentUserId == userId)
            return;
        throw new UnauthorizedAccessException("Bạn không có quyền truy cập buổi học này.");
    }

    public async Task<SessionScheduleConflictResponse?> FindCurrentConflictAsync(
        int classSessionId,
        DateTime candidateStart,
        CancellationToken cancellationToken = default)
    {
        var session = await LoadSessionAsync(classSessionId, cancellationToken)
            ?? throw new KeyNotFoundException("Không tìm thấy buổi học.");
        var approvedChange = await db.ClassSessionScheduleChanges
            .AsNoTracking()
            .Where(x => x.Classsessionid == classSessionId
                && x.Status == ScheduleChangeStatus.Approved
                && x.Expiresat > candidateStart)
            .OrderByDescending(x => x.Schedulechangeid)
            .FirstOrDefaultAsync(cancellationToken);

        if (approvedChange == null)
            return null;

        var candidateEnd = candidateStart.Add(
            approvedChange.Originalscheduledend - approvedChange.Originalscheduledstart);
        return await ClassSessionScheduleConflictGuard.FindAsync(
            db,
            session.Classsessionid,
            session.Tutorid,
            session.Studentid,
            candidateStart,
            candidateEnd,
            cancellationToken,
            approvedChange.Approvedat,
            approvedChange.Schedulechangeid);
    }

    private async Task NotifyApproversAsync(
        SessionSnapshot session,
        ClassSessionScheduleChange change,
        string requesterId)
    {
        foreach (var target in new[] { change.Tutoruserid, change.Learnerapproveruserid }.Distinct())
        {
            if (target == requesterId) continue;
            try
            {
                await notificationService.CreateNotificationAsync(new NotificationRequest
                {
                    Userid = target,
                    Title = "Cần xác nhận đổi giờ học",
                    Message = target == change.Learnerapproveruserid
                        && string.Equals(change.Learnerapproverrole, UserRole.Parent, StringComparison.OrdinalIgnoreCase)
                        ? $"Buổi học của {session.StudentName ?? "học viên"} đang được mở ngoài lịch dự kiến. Vui lòng mở chi tiết buổi học để xác nhận."
                        : $"Buổi học của {session.StudentName ?? "học viên"} đang được mở ngoài lịch dự kiến. Vui lòng vào phòng chờ để xác nhận.",
                    Type = NotificationType.LessonScheduleChange,
                    Referenceid = session.Classsessionid.ToString()
                });
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not notify schedule-change approver {UserId}", target);
            }
        }
    }

    private async Task NotifyExpiredAsync(SessionSnapshot session, ClassSessionScheduleChange change)
    {
        foreach (var target in new[] { change.Tutoruserid, change.Learnerapproveruserid }.Distinct())
        {
            try
            {
                await notificationService.CreateNotificationAsync(new NotificationRequest
                {
                    Userid = target,
                    Title = "Yêu cầu xác nhận đổi giờ đã hết hạn",
                    Message = target == change.Tutoruserid
                        ? $"Yêu cầu xác nhận học ngoài lịch của {session.StudentName ?? "học viên"} đã hết hạn vì chưa đủ hai bên xác nhận trong 30 phút. Vui lòng vào lại phòng chờ để gửi yêu cầu mới."
                        : $"Yêu cầu xác nhận học ngoài lịch của {session.StudentName ?? "học viên"} đã hết hạn. Gia sư cần mở lại phòng chờ để gửi yêu cầu mới.",
                    Type = NotificationType.LessonScheduleChange,
                    Referenceid = session.Classsessionid.ToString()
                });
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not notify schedule-change expiry to {UserId}", target);
            }
        }
    }

    private static SessionScheduleChangeResponse Map(
        SessionSnapshot session,
        ClassSessionScheduleChange? change,
        string userId,
        bool requiresConfirmation,
        bool admissionAllowed)
    {
        var approver = ResolveLearnerApprover(session);
        var requiredLearnerRole = change?.Learnerapproverrole ?? approver.Role;
        var requiredLearnerName = string.Equals(requiredLearnerRole, UserRole.Parent, StringComparison.OrdinalIgnoreCase)
            ? session.ParentName ?? "Phụ huynh"
            : session.StudentName ?? "Học sinh";
        return new SessionScheduleChangeResponse
        {
            ClassSessionId = session.Classsessionid,
            RequiresConfirmation = requiresConfirmation,
            CanCurrentUserConfirm = requiresConfirmation
                && change?.Status == ScheduleChangeStatus.Pending
                && (change.Tutoruserid == userId || change.Learnerapproveruserid == userId),
            CurrentUserConfirmed = change != null
                && ((change.Tutoruserid == userId && change.Tutorconfirmedat.HasValue)
                    || (change.Learnerapproveruserid == userId && change.Learnerconfirmedat.HasValue)),
            AdmissionAllowed = admissionAllowed,
            Status = change?.Status,
            TutorUserId = change?.Tutoruserid ?? session.Tutorid,
            LearnerApproverUserId = change?.Learnerapproveruserid ?? approver.UserId,
            RequiredLearnerRole = requiredLearnerRole,
            RequiredLearnerName = requiredLearnerName,
            TutorName = session.TutorName ?? "Gia sư",
            StudentName = session.StudentName ?? "Học sinh",
            OriginalScheduledStart = change?.Originalscheduledstart ?? session.Scheduledstart,
            OriginalScheduledEnd = change?.Originalscheduledend ?? session.Scheduledend,
            DurationMinutes = Math.Max(1, (int)Math.Round(
                ((change?.Originalscheduledend ?? session.Scheduledend)
                    - (change?.Originalscheduledstart ?? session.Scheduledstart)).TotalMinutes)),
            RequestedAt = change?.Requestedat,
            ExpiresAt = change?.Expiresat,
            TutorConfirmedAt = change?.Tutorconfirmedat,
            LearnerConfirmedAt = change?.Learnerconfirmedat,
            RejectedByUserId = change?.Rejectedby,
            ApprovedAt = change?.Approvedat,
            AppliedAt = change?.Appliedat,
            AdjustedScheduledStart = change?.Adjustedscheduledstart,
            AdjustedScheduledEnd = change?.Adjustedscheduledend
        };
    }

    private sealed record SessionSnapshot(
        int Classsessionid,
        string? Status,
        DateTime Scheduledstart,
        DateTime Scheduledend,
        string? Tutorid,
        string? TutorName,
        string? Studentid,
        string? ParentId,
        string? ParentName,
        string? StudentUserId,
        string? StudentName,
        DateOnly? StudentBirthdate,
        bool Iscontinuation);
}
