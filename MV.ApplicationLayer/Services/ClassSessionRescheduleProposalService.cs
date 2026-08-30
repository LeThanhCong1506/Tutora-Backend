using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.Helpers;
using MV.ApplicationLayer.Hubs;
using MV.ApplicationLayer.Interfaces;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Entities;
using MV.DomainLayer.Helpers;

namespace MV.ApplicationLayer.Services;

public class ClassSessionRescheduleProposalService(
    IAppDbContext db,
    INotificationService notificationService,
    IHubContext<SessionLobbyHub> lobbyHubContext,
    ILogger<ClassSessionRescheduleProposalService> logger) : IClassSessionRescheduleProposalService
{
    private const int MinHoursBeforeOriginalStart = 2;
    private const int MaxProposalLifetimeHours = 24;

    public async Task<ClassSessionRescheduleProposalResponse> ProposeAsync(
        int classSessionId,
        string userId,
        string? role,
        DateTime proposedScheduledStart,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        var session = await LoadSessionAsync(classSessionId, cancellationToken)
            ?? throw new KeyNotFoundException("Không tìm thấy buổi học.");

        if (session.Status != ClassSessionStatus.Scheduled)
            throw new InvalidOperationException("Chỉ có thể đề xuất đổi lịch cho buổi học đang ở trạng thái đã lên lịch.");

        // Cả 2 bên đã đồng ý bỏ hẳn buổi phụ này (ConfirmSkipContinuationAsync) — status vẫn
        // "scheduled" cho tới khi báo cáo buổi gốc được nộp, nhưng buổi coi như đã "chết" nên
        // không cho đề xuất đổi lịch mới (mâu thuẫn với ý định đã thống nhất bỏ buổi này).
        if (session.Tutorskipconfirmedat.HasValue && session.Studentskipconfirmedat.HasValue)
            throw new InvalidOperationException("Cả 2 bên đã đồng ý bỏ buổi phụ này, không thể đề xuất đổi lịch nữa.");

        var approver = ResolveLearnerApprover(session);
        var now = TimeZoneHelper.UtcNow;

        string proposerRole;
        string counterpartUserId;
        string counterpartRole;
        if (session.Tutorid == userId)
        {
            proposerRole = UserRole.Tutor;
            counterpartUserId = approver.UserId;
            counterpartRole = approver.Role;
        }
        else if (IsLearnerSideActor(session, userId))
        {
            // Học sinh do phụ huynh quản lý được tự đề xuất đổi lịch (trước đây chỉ phụ huynh mới
            // được, xem IsLearnerSideActor) — không gate theo tuổi như nhánh tự quản lý vì phụ huynh
            // đã đứng ra chịu trách nhiệm cho tài khoản này.
            proposerRole = LearnerActorRole(session, userId);
            counterpartUserId = session.Tutorid ?? string.Empty;
            counterpartRole = UserRole.Tutor;
        }
        else
        {
            throw new UnauthorizedAccessException("Bạn không có quyền đề xuất đổi lịch cho buổi học này.");
        }

        if (string.IsNullOrWhiteSpace(counterpartUserId))
            throw new InvalidOperationException("Buổi học thiếu thông tin người nhận đề xuất.");

        if (proposedScheduledStart <= now)
            throw new InvalidOperationException("Thời gian đề xuất phải ở tương lai.");

        // Buổi phụ (Iscontinuation, sinh ra khi buổi gốc bị ngắt giữa chừng vì sự cố đột xuất) là
        // tình huống khẩn cấp — bỏ buffer 2h trước giờ học vì có thể cần dời trong vài chục phút.
        // Đổi lại, nó chỉ được phép diễn ra trong ĐÚNG NGÀY bị ngắt (UTC thuần, hệ thống không quy
        // đổi múi giờ), không được kéo qua ngày hôm sau.
        DateTime proposalCutoff;
        if (session.Iscontinuation)
        {
            proposalCutoff = (session.Interruptedat ?? now).Date.AddDays(1);
            if (now >= proposalCutoff)
                throw new InvalidOperationException(
                    "Đã quá ngày bị gián đoạn, không thể đề xuất giờ học lại cho buổi phụ này nữa.");
            if (proposedScheduledStart >= proposalCutoff)
                throw new InvalidOperationException(
                    "Buổi học phụ chỉ có thể diễn ra trong cùng ngày bị gián đoạn.");
        }
        else
        {
            proposalCutoff = session.Scheduledstart.AddHours(-MinHoursBeforeOriginalStart);
            if (now >= proposalCutoff)
                throw new InvalidOperationException(
                    $"Không thể đề xuất đổi lịch khi còn dưới {MinHoursBeforeOriginalStart} giờ trước giờ học đã đặt.");
        }

        var existingPending = await db.ClassSessionRescheduleProposals
            .Where(x => x.Classsessionid == classSessionId && x.Status == RescheduleProposalStatus.Pending)
            .AnyAsync(cancellationToken);
        if (existingPending)
            throw new InvalidOperationException("Buổi học này đang có một đề xuất đổi lịch chờ phản hồi.");

        // Chặn chiều ngược lại: nếu đang có yêu cầu xác nhận vào học ngoài giờ (cơ chế cũ,
        // ClassSessionScheduleChange) còn hiệu lực, không cho tạo đề xuất đổi lịch mới — tránh 2
        // cơ chế đổi giờ cùng hoạt động trên 1 buổi học rồi ghi đè lẫn nhau.
        var hasActiveScheduleChange = await db.ClassSessionScheduleChanges
            .AnyAsync(x => x.Classsessionid == classSessionId
                && (x.Status == ScheduleChangeStatus.Pending || x.Status == ScheduleChangeStatus.Approved)
                && x.Expiresat > now, cancellationToken);
        if (hasActiveScheduleChange)
            throw new InvalidOperationException(
                "Buổi học đang có yêu cầu xác nhận vào học ngoài giờ, vui lòng xử lý xong trước khi đề xuất đổi lịch.");

        var duration = session.Scheduledend - session.Scheduledstart;
        var proposedScheduledEnd = proposedScheduledStart.Add(duration);

        var conflict = await ClassSessionScheduleConflictGuard.FindAsync(
            db, classSessionId, session.Tutorid, session.Studentid,
            proposedScheduledStart, proposedScheduledEnd, cancellationToken);
        if (conflict != null)
            throw new InvalidOperationException(conflict.Message);

        var expiresAt = new[] { now.AddHours(MaxProposalLifetimeHours), proposalCutoff }.Min();

        var proposal = new ClassSessionRescheduleProposal
        {
            Classsessionid = classSessionId,
            Proposedbyuserid = userId,
            Proposedbyrole = proposerRole,
            Counterpartuserid = counterpartUserId,
            Counterpartrole = counterpartRole,
            Originalscheduledstart = session.Scheduledstart,
            Originalscheduledend = session.Scheduledend,
            Proposedscheduledstart = proposedScheduledStart,
            Proposedscheduledend = proposedScheduledEnd,
            Reason = reason,
            Status = RescheduleProposalStatus.Pending,
            Requestedat = now,
            Expiresat = expiresAt,
            Createdat = now,
            Updatedat = now
        };
        db.ClassSessionRescheduleProposals.Add(proposal);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            throw new InvalidOperationException("Buổi học này đang có một đề xuất đổi lịch chờ phản hồi.");
        }

        await NotifyAsync(
            counterpartUserId,
            "Có đề xuất đổi lịch học mới",
            $"{DisplayName(session, userId)} đề xuất dời buổi học #{classSessionId} sang " +
                $"{FormatVietnamTime(proposedScheduledStart)}. Vui lòng vào chi tiết buổi học để phản hồi.",
            NotificationType.RescheduleProposed,
            classSessionId);

        if (proposerRole == UserRole.Student && !string.IsNullOrWhiteSpace(session.ParentId))
        {
            await NotifyAsync(
                session.ParentId!,
                "Con bạn đã đề xuất đổi lịch học",
                $"Con bạn đã đề xuất dời buổi học #{classSessionId} sang {FormatVietnamTime(proposedScheduledStart)}.",
                NotificationType.RescheduleProposed,
                classSessionId);
        }

        return await MapAsync(proposal, session, cancellationToken);
    }

    public async Task<ClassSessionRescheduleProposalResponse> RespondAsync(
        int classSessionId,
        string userId,
        string? role,
        bool accepted,
        CancellationToken cancellationToken = default)
    {
        var session = await LoadSessionAsync(classSessionId, cancellationToken)
            ?? throw new KeyNotFoundException("Không tìm thấy buổi học.");

        // Cả 2 bên đã đồng ý bỏ hẳn buổi phụ này trong lúc đề xuất còn treo — coi như đã hết hiệu
        // lực, không cho phản hồi nữa (xem guard tương ứng ở ProposeAsync).
        if (session.Tutorskipconfirmedat.HasValue && session.Studentskipconfirmedat.HasValue)
            throw new InvalidOperationException("Cả 2 bên đã đồng ý bỏ buổi phụ này, đề xuất đổi lịch không còn hiệu lực.");

        var proposal = await db.ClassSessionRescheduleProposals
            .Where(x => x.Classsessionid == classSessionId && x.Status == RescheduleProposalStatus.Pending)
            .OrderByDescending(x => x.Rescheduleproposalid)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("Không có đề xuất đổi lịch nào đang chờ phản hồi.");

        var now = TimeZoneHelper.UtcNow;
        if (proposal.Expiresat <= now)
        {
            proposal.Status = RescheduleProposalStatus.Expired;
            proposal.Updatedat = now;
            await db.SaveChangesAsync(cancellationToken);
            throw new InvalidOperationException("Đề xuất đổi lịch đã hết hạn.");
        }

        // Cho phép đúng người được lưu làm counterpart PHẢN HỒI, hoặc — khi counterpart là phụ
        // huynh — chính học sinh do phụ huynh đó quản lý (đối xứng với nhánh đề xuất ở ProposeAsync).
        var respondedByManagedStudent = proposal.Counterpartrole == UserRole.Parent
            && IsLearnerSideActor(session, userId)
            && userId != proposal.Counterpartuserid;
        if (proposal.Counterpartuserid != userId && !respondedByManagedStudent)
            throw new UnauthorizedAccessException("Bạn không phải người có quyền phản hồi đề xuất này.");

        if (respondedByManagedStudent && !string.IsNullOrWhiteSpace(session.ParentId))
        {
            await NotifyAsync(
                session.ParentId!,
                "Con bạn đã phản hồi đề xuất đổi lịch",
                accepted
                    ? $"Con bạn đã đồng ý dời buổi học #{classSessionId} sang {FormatVietnamTime(proposal.Proposedscheduledstart)}."
                    : $"Con bạn đã từ chối đề xuất dời buổi học #{classSessionId}.",
                accepted ? NotificationType.RescheduleAccepted : NotificationType.RescheduleRejected,
                classSessionId);
        }

        if (!accepted)
        {
            proposal.Status = RescheduleProposalStatus.Rejected;
            proposal.Respondedat = now;
            proposal.Respondedby = userId;
            proposal.Updatedat = now;
            await db.SaveChangesAsync(cancellationToken);

            await NotifyAsync(
                proposal.Proposedbyuserid,
                "Đề xuất đổi lịch bị từ chối",
                $"Đề xuất dời buổi học #{classSessionId} sang {FormatVietnamTime(proposal.Proposedscheduledstart)} đã bị từ chối.",
                NotificationType.RescheduleRejected,
                classSessionId);

            // Báo cho lobby biết ngay (nếu có ai đang chờ trong đó) thay vì để họ đứng sau banner
            // khoá tới 10s (chu kỳ RefreshState) mới tự gỡ — xem SessionLobbyHub cho ngữ cảnh group.
            await BroadcastRescheduleRejectedAsync(classSessionId, cancellationToken);

            return await MapAsync(proposal, session, cancellationToken);
        }

        var conflict = await ClassSessionScheduleConflictGuard.FindAsync(
            db, classSessionId, session.Tutorid, session.Studentid,
            proposal.Proposedscheduledstart, proposal.Proposedscheduledend, cancellationToken);
        if (conflict != null)
            throw new InvalidOperationException(conflict.Message);

        var sessionRow = await db.ClassSessions
            .FirstOrDefaultAsync(x => x.Classsessionid == classSessionId && x.Status == ClassSessionStatus.Scheduled, cancellationToken)
            ?? throw new InvalidOperationException("Buổi học không còn ở trạng thái có thể đổi lịch.");

        sessionRow.Scheduledstart = proposal.Proposedscheduledstart;
        sessionRow.Scheduledend = proposal.Proposedscheduledend;

        proposal.Status = RescheduleProposalStatus.Accepted;
        proposal.Respondedat = now;
        proposal.Respondedby = userId;
        proposal.Updatedat = now;
        await db.SaveChangesAsync(cancellationToken);

        await NotifyAsync(
            proposal.Proposedbyuserid,
            "Đề xuất đổi lịch đã được đồng ý",
            $"Buổi học #{classSessionId} đã được dời sang {FormatVietnamTime(proposal.Proposedscheduledstart)}.",
            NotificationType.RescheduleAccepted,
            classSessionId);

        return await MapAsync(proposal, session with { Scheduledstart = proposal.Proposedscheduledstart, Scheduledend = proposal.Proposedscheduledend }, cancellationToken);
    }

    public async Task<List<ClassSessionRescheduleProposalResponse>> GetProposalHistoryAsync(
        int classSessionId,
        CancellationToken cancellationToken = default)
    {
        var session = await LoadSessionAsync(classSessionId, cancellationToken);
        if (session == null) return new List<ClassSessionRescheduleProposalResponse>();

        var proposals = await db.ClassSessionRescheduleProposals
            .AsNoTracking()
            .Where(x => x.Classsessionid == classSessionId)
            .OrderByDescending(x => x.Rescheduleproposalid)
            .ToListAsync(cancellationToken);

        var result = new List<ClassSessionRescheduleProposalResponse>(proposals.Count);
        foreach (var proposal in proposals)
            result.Add(await MapAsync(proposal, session, cancellationToken));
        return result;
    }

    public async Task<int> ExpireOverdueProposalsAsync(CancellationToken cancellationToken = default)
    {
        var now = TimeZoneHelper.UtcNow;
        var overdue = await db.ClassSessionRescheduleProposals
            .Where(x => x.Status == RescheduleProposalStatus.Pending && x.Expiresat <= now)
            .ToListAsync(cancellationToken);

        foreach (var proposal in overdue)
        {
            proposal.Status = RescheduleProposalStatus.Expired;
            proposal.Updatedat = now;
        }
        if (overdue.Count > 0)
            await db.SaveChangesAsync(cancellationToken);

        foreach (var proposal in overdue)
        {
            try
            {
                await notificationService.CreateNotificationAsync(new MV.DomainLayer.DTO.RequestModel.NotificationRequest
                {
                    Userid = proposal.Proposedbyuserid,
                    Title = "Đề xuất đổi lịch đã hết hạn",
                    Message = $"Đề xuất dời buổi học #{proposal.Classsessionid} sang " +
                        $"{FormatVietnamTime(proposal.Proposedscheduledstart)} đã hết hạn do không có phản hồi.",
                    Type = NotificationType.RescheduleExpired,
                    Referenceid = proposal.Classsessionid.ToString()
                });
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not notify reschedule-proposal expiry for proposal {ProposalId}", proposal.Rescheduleproposalid);
            }
        }

        return overdue.Count;
    }

    /// <summary>Best-effort — không được để lỗi broadcast làm hỏng thao tác từ chối đã lưu DB thành công.</summary>
    private async Task BroadcastRescheduleRejectedAsync(int classSessionId, CancellationToken cancellationToken)
    {
        try
        {
            await lobbyHubContext.Clients.Group($"lobby:{classSessionId}")
                .SendAsync("rescheduleProposalRejected", new { classSessionId }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not broadcast reschedule-proposal rejection to lobby for classSession {ClassSessionId}", classSessionId);
        }
    }

    private async Task NotifyAsync(string userId, string title, string message, string type, int classSessionId)
    {
        try
        {
            await notificationService.CreateNotificationAsync(new MV.DomainLayer.DTO.RequestModel.NotificationRequest
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
            logger.LogWarning(ex, "Could not notify reschedule-proposal event for user {UserId}", userId);
        }
    }

    private static string FormatVietnamTime(DateTime utc)
    {
        var vietnamTimeZone = TimeZoneHelper.GetTimeZoneInfo("Asia/Ho_Chi_Minh");
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), vietnamTimeZone);
        return $"{local:HH:mm} ngày {local:dd/MM/yyyy}";
    }

    private static string DisplayName(SessionSnapshot session, string requesterId)
        => requesterId == session.Tutorid
            ? session.TutorName ?? "Gia sư"
            : requesterId == session.ParentId
                ? session.ParentName ?? "Phụ huynh"
                : session.StudentName ?? "Học sinh";

    private async Task<ClassSessionRescheduleProposalResponse> MapAsync(
        ClassSessionRescheduleProposal proposal,
        SessionSnapshot session,
        CancellationToken cancellationToken)
    {
        var names = await db.Users.AsNoTracking()
            .Where(x => x.Userid == proposal.Proposedbyuserid || x.Userid == proposal.Counterpartuserid)
            .ToDictionaryAsync(x => x.Userid, x => x.Fullname ?? x.Username ?? x.Email, cancellationToken);

        return new ClassSessionRescheduleProposalResponse
        {
            RescheduleProposalId = proposal.Rescheduleproposalid,
            ClassSessionId = proposal.Classsessionid,
            ProposedByUserId = proposal.Proposedbyuserid,
            ProposedByRole = proposal.Proposedbyrole,
            ProposedByName = names.TryGetValue(proposal.Proposedbyuserid, out var proposerName) ? proposerName : null,
            CounterpartUserId = proposal.Counterpartuserid,
            CounterpartRole = proposal.Counterpartrole,
            CounterpartName = names.TryGetValue(proposal.Counterpartuserid, out var counterpartName) ? counterpartName : null,
            OriginalScheduledStart = proposal.Originalscheduledstart,
            OriginalScheduledEnd = proposal.Originalscheduledend,
            ProposedScheduledStart = proposal.Proposedscheduledstart,
            ProposedScheduledEnd = proposal.Proposedscheduledend,
            Reason = proposal.Reason,
            Status = proposal.Status,
            RequestedAt = proposal.Requestedat,
            ExpiresAt = proposal.Expiresat,
            RespondedAt = proposal.Respondedat
        };
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
                x.Iscontinuation,
                x.Interruptedat,
                x.Tutorskipconfirmedat,
                x.Studentskipconfirmedat))
            .FirstOrDefaultAsync(cancellationToken);

    // Nhân bản có chủ đích từ ClassSessionScheduleChangeService.ResolveLearnerApprover (private static ở
    // đó, không gọi trực tiếp được) — cùng luật: phụ huynh nếu có, không thì học sinh nếu đủ tuổi tự đặt.
    private static (string UserId, string Role, string Name) ResolveLearnerApprover(SessionSnapshot session)
    {
        if (!string.IsNullOrWhiteSpace(session.ParentId))
            return (session.ParentId!, UserRole.Parent, session.ParentName ?? "Phụ huynh");

        var studentCanConfirm = AgeHelper.IsOldEnoughToSelfBook(session.StudentBirthdate)
            && !string.IsNullOrWhiteSpace(session.StudentUserId);
        if (studentCanConfirm)
            return (session.StudentUserId!, UserRole.Student, session.StudentName ?? "Học sinh");

        return (string.Empty, UserRole.Parent, "Phụ huynh");
    }

    // Học sinh do phụ huynh quản lý (session.ParentId có giá trị) VẪN được coi là actor hợp lệ
    // ở phía "learner", song song với phụ huynh — khác với ResolveLearnerApprover (chỉ trả về MỘT
    // approver "chính" để dùng làm counterpart lưu DB khi gia sư là người đề xuất).
    private static bool IsLearnerSideActor(SessionSnapshot session, string userId)
        => (!string.IsNullOrWhiteSpace(session.ParentId) && session.ParentId == userId)
            || (!string.IsNullOrWhiteSpace(session.StudentUserId) && session.StudentUserId == userId);

    private static string LearnerActorRole(SessionSnapshot session, string userId)
        => !string.IsNullOrWhiteSpace(session.StudentUserId) && session.StudentUserId == userId
            ? UserRole.Student
            : UserRole.Parent;

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
        bool Iscontinuation,
        DateTime? Interruptedat,
        DateTime? Tutorskipconfirmedat,
        DateTime? Studentskipconfirmedat);
}
