using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
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
    private const int EarlyJoinToleranceMinutes = 15;
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
            return Map(session, latest, userId, true, true);
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
        if (!confirmed)
        {
            change.Status = ScheduleChangeStatus.Rejected;
            change.Rejectedat = now;
            change.Rejectedby = userId;
            change.Updatedat = now;
            await db.SaveChangesAsync(cancellationToken);
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
            await EnsureNoScheduleConflictAsync(session, change, now, cancellationToken);
            change.Status = ScheduleChangeStatus.Approved;
            change.Approvedat = now;
        }
        change.Updatedat = now;
        await db.SaveChangesAsync(cancellationToken);
        return Map(session, change, userId, true, change.Status == ScheduleChangeStatus.Approved);
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
                        : null))
            .FirstOrDefaultAsync(cancellationToken);

    private static bool IsWithinNormalWindow(SessionSnapshot session, DateTime now)
        => now >= session.Scheduledstart.AddMinutes(-EarlyJoinToleranceMinutes)
            && now <= session.Scheduledend;

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

    private async Task EnsureNoScheduleConflictAsync(
        SessionSnapshot session,
        ClassSessionScheduleChange change,
        DateTime candidateStart,
        CancellationToken cancellationToken)
    {
        var candidateEnd = candidateStart.Add(change.Originalscheduledend - change.Originalscheduledstart);
        var conflict = await db.ClassSessions.AsNoTracking().AnyAsync(
            x => x.Classsessionid != session.Classsessionid
                && x.Checkouttime == null
                && (x.Status == ClassSessionStatus.Scheduled || x.Status == ClassSessionStatus.InProgress)
                && (x.Tutorid == session.Tutorid || x.Studentid == session.Studentid)
                && x.Scheduledstart < candidateEnd
                && x.Scheduledend > candidateStart,
            cancellationToken);
        if (conflict)
            throw new InvalidOperationException("Thời gian học mới bị trùng với một buổi học khác của gia sư hoặc học viên.");
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
        DateOnly? StudentBirthdate);
}
