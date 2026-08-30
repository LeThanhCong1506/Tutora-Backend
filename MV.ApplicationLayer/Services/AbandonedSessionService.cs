using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MV.ApplicationLayer.Helpers;
using MV.ApplicationLayer.Interfaces;
using MV.ApplicationLayer.RepositoryInterfaces;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Configuration;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.Entities;
using MV.DomainLayer.Helpers;
using static MV.DomainLayer.Constants.ClassSessionStatus;

namespace MV.ApplicationLayer.Services;

/// <inheritdoc/>
public class AbandonedSessionService : IAbandonedSessionService
{
    /// <summary>Lượt lobby chỉ được tính nếu bắt đầu trong khoảng này trước giờ học.</summary>
    private const int LobbyWindowLeadMinutes = 15;

    private readonly IAppDbContext _context;
    private readonly INotificationService _notificationService;
    private readonly INotificationRepository _notificationRepo;
    private readonly AbandonedSessionSettings _settings;
    private readonly ILogger<AbandonedSessionService> _logger;

    public AbandonedSessionService(
        IAppDbContext context,
        INotificationService notificationService,
        INotificationRepository notificationRepo,
        IOptions<AbandonedSessionSettings> settings,
        ILogger<AbandonedSessionService> logger)
    {
        _context = context;
        _notificationService = notificationService;
        _notificationRepo = notificationRepo;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<int> ProcessAbandonedSessionsAsync(CancellationToken ct = default)
    {
        var now = TimeZoneHelper.UtcNow;
        var cutoff = now.AddHours(-_settings.NoticeDelayHours);

        // Buổi PHỤ (Iscontinuation) bị loại: Scheduledend của nó chỉ là mốc ước tính lúc tạo
        // (now + 1h, xem BuildContinuationSession), không phải cam kết giờ học thật — đo "quá giờ"
        // theo mốc đó sẽ kết luận sai. Buổi phụ chết được SubmitReportAsync tự huỷ theo đường riêng.
        var candidates = await _context.ClassSessions
            .Include(l => l.Booking)
                .ThenInclude(b => b!.Student)
            .Where(l => l.Status == Scheduled
                && !l.Iscontinuation
                && l.Scheduledend <= cutoff)
            .ToListAsync(ct);

        if (candidates.Count == 0) return 0;

        var touched = 0;
        foreach (var classSession in candidates)
        {
            try
            {
                if (await ProcessOneAsync(classSession, now, ct))
                    touched++;
            }
            catch (Exception ex)
            {
                // Một buổi hỏng không được chặn các buổi còn lại.
                _logger.LogError(ex,
                    "Không xử lý được buổi học bị bỏ quên {ClassSessionId}.",
                    classSession.Classsessionid);
            }
        }

        return touched;
    }

    private async Task<bool> ProcessOneAsync(ClassSession classSession, DateTime now, CancellationToken ct)
    {
        if (DisputeSettlementPolicy.IsTerminalBooking(classSession.Booking?.Status))
            return false;

        // Buổi đang có tranh chấp mở đã nằm trong tay admin — không tự động đụng vào.
        var hasOpenDispute = await _context.Disputes.AnyAsync(
            d => d.Classsessionid == classSession.Classsessionid
                && d.Status != DisputeStatus.Resolved
                && d.Status != DisputeStatus.Closed,
            ct);
        if (hasOpenDispute) return false;

        var attendance = await ResolveAttendanceAsync(classSession, ct);

        return attendance.AnyoneAttended
            ? await FlagForAdminAsync(classSession, attendance, now, ct)
            : await StartConfirmationWindowAsync(classSession, now, ct);
    }

    // ── Nhánh 1: không ai đến ────────────────────────────────────────────────

    /// <summary>
    /// Không ai vào lớp và cũng không ai khiếu nại. Đưa buổi về đúng luồng xác nhận bình thường:
    /// đặt hạn xác nhận rồi để <c>ProcessAutoConfirmAsync</c> (đã chạy production) settle nếu hết
    /// hạn mà vẫn không có tranh chấp. Không phát sinh nhánh chuyển tiền mới.
    ///
    /// Hoàn tiền cho phụ huynh ở đây sẽ thưởng cho việc hai bên hẹn nhau học ngoài nền tảng rồi
    /// cùng bỏ phòng học; auto-confirm thì ngược lại — trốn ra ngoài không được lợi gì.
    /// </summary>
    private async Task<bool> StartConfirmationWindowAsync(ClassSession classSession, DateTime now, CancellationToken ct)
    {
        classSession.Status = PendingConfirmation;
        classSession.Confirmdeadline = now.AddHours(_settings.ResponseWindowHours);
        classSession.Noshowaction = NoShowActionTypes.AutoNoAttendance;
        classSession.Istutorpresent = false;
        classSession.Isstudentpresent = false;

        await _context.SaveChangesAsync(ct);

        // Im lặng chỉ mang nghĩa đồng ý nếu người ta BIẾT mình đang đồng ý với cái gì. Không có
        // thông báo này thì auto-confirm là chuyển tiền âm thầm từ phụ huynh sang gia sư.
        // Định dạng giờ khớp với ClassSessionReminderJob để thông báo trong app đọc nhất quán.
        var deadlineText = classSession.Confirmdeadline.Value.ToString("HH:mm dd/MM/yyyy");
        var startText = classSession.Scheduledstart.ToString("HH:mm dd/MM");
        var message =
            $"Buổi học #{classSession.Classsessionid} ngày {startText} không ghi nhận ai vào lớp. "
            + $"Nếu không có phản hồi trước {deadlineText}, buổi sẽ được tính là đã hoàn thành.";

        foreach (var userId in ResolveNotifyTargets(classSession))
            await NotifyOnceAsync(userId, classSession.Classsessionid, "Buổi học không có ai tham dự", message);

        _logger.LogInformation(
            "Buổi {ClassSessionId} không ghi nhận ai vào lớp — mở cửa sổ xác nhận tới {Deadline:o}.",
            classSession.Classsessionid, classSession.Confirmdeadline);

        return true;
    }

    // ── Nhánh 2: một bên có đến ──────────────────────────────────────────────

    /// <summary>
    /// Có người đến mà buổi vẫn không diễn ra — có người chịu thiệt, và quy trách nhiệm là việc
    /// của con người. Tạo dispute do HỆ THỐNG mở (<c>Createdby = null</c>) để ca này hiện trong
    /// trang xem xét khiếu nại cùng đầy đủ công cụ bằng chứng, và giữ nguyên <c>Scheduled</c> để
    /// đường tự khiếu nại của phụ huynh vẫn mở (ReportTutorNoShowAsync đòi đúng trạng thái này).
    /// </summary>
    private async Task<bool> FlagForAdminAsync(
        ClassSession classSession,
        SessionAttendance attendance,
        DateTime now,
        CancellationToken ct)
    {
        // Đã từng gắn cờ cho buổi này (dispute cũ đã đóng) thì thôi, tránh mở lại vòng lặp.
        var alreadyFlagged = await _context.Disputes.AnyAsync(
            d => d.Classsessionid == classSession.Classsessionid && d.Createdby == null, ct);
        if (alreadyFlagged) return false;

        // Lặp lại trong cùng một booking là tín hiệu đáng ngờ hơn hẳn một buổi lẻ: gia sư no-show
        // thật sẽ bị phụ huynh đòi đổi người từ lần thứ hai, còn cặp hẹn học ngoài thì không ai
        // phàn nàn. Admin đọc tab "Chat buổi học" để phân biệt.
        var priorFlags = await _context.Disputes.CountAsync(
            d => d.Bookingid == classSession.Bookingid && d.Createdby == null, ct);

        var dispute = new Dispute
        {
            Classsessionid = classSession.Classsessionid,
            Bookingid = classSession.Bookingid,
            Createdby = null,
            Disputetype = DisputeTypes.NoShow,
            Status = DisputeStatus.Pending,
            Reason = attendance.TutorAttended
                ? "Hệ thống phát hiện: gia sư có mặt nhưng học viên không vào lớp."
                : "Hệ thống phát hiện: học viên có mặt nhưng gia sư không vào lớp.",
            Evidence = JsonSerializer.Serialize(attendance.Evidence),
            Priorityreason = priorFlags > 0
                ? $"Buổi thứ {priorFlags + 1} của booking này bị hệ thống gắn cờ — "
                  + "nghi ngờ hai bên giao dịch ngoài nền tảng, cần đọc lịch sử chat trước khi quyết."
                : null,
            Createdat = now
        };

        _context.Disputes.Add(dispute);
        await _context.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Buổi {ClassSessionId} chỉ có một bên đến (tutor={TutorAttended}) — đã tạo dispute hệ thống, "
            + "cờ nghi ngờ={Suspicious}.",
            classSession.Classsessionid, attendance.TutorAttended, priorFlags > 0);

        return true;
    }

    // ── Xác định ai đã thực sự đến ───────────────────────────────────────────

    /// <summary>
    /// Ba nguồn, mạnh dần: lượt lobby đủ lâu (đã đến chờ) → admission (đã xin token vào phòng) →
    /// presence interval (thật sự ở trong phòng). Bất kỳ nguồn nào cũng đủ để coi là "đã đến";
    /// riêng lobby phải vượt ngưỡng thời gian vì nó là tín hiệu rẻ nhất và dễ tạo ra nhất.
    /// </summary>
    private async Task<SessionAttendance> ResolveAttendanceAsync(ClassSession classSession, CancellationToken ct)
    {
        var sessionId = classSession.Classsessionid;

        var presenceRoles = await _context.SessionPresenceIntervals
            .Where(p => p.ClassSessionId == sessionId)
            .Select(p => p.Role)
            .Distinct()
            .ToListAsync(ct);

        var admissionRoles = await _context.SessionParticipants
            .Where(p => p.ClassSessionId == sessionId)
            .Select(p => p.Role)
            .Distinct()
            .ToListAsync(ct);

        var windowStart = classSession.Scheduledstart.AddMinutes(-LobbyWindowLeadMinutes);
        var lobbyVisits = await _context.SessionLobbyVisits
            .Where(v => v.ClassSessionId == sessionId
                && v.EnteredAt >= windowStart
                && v.EnteredAt <= classSession.Scheduledend)
            .Select(v => new LobbyVisitWindow(v.Role, v.EnteredAt, v.LastSeenAt))
            .ToListAsync(ct);

        // Mạng chập chờn cắt một lượt chờ thành nhiều dòng, nên cộng dồn theo vai trò.
        var qualifyingLobbyRoles = lobbyVisits
            .GroupBy(v => v.Role)
            .Where(g => g.Sum(v => (v.LastSeenAt - v.EnteredAt).TotalMinutes) >= _settings.LobbyPresenceMinimumMinutes)
            .Select(g => g.Key)
            .ToList();

        var attendedRoles = presenceRoles
            .Concat(admissionRoles)
            .Concat(qualifyingLobbyRoles)
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var tutorAttended = attendedRoles.Contains(SessionParticipantRole.Tutor, StringComparer.OrdinalIgnoreCase);
        // Phụ huynh vào thay học viên vẫn tính là bên học viên đã đến.
        var learnerAttended =
            attendedRoles.Contains(SessionParticipantRole.Student, StringComparer.OrdinalIgnoreCase)
            || attendedRoles.Contains(SessionParticipantRole.Parent, StringComparer.OrdinalIgnoreCase);

        var evidence = new
        {
            detectedAt = TimeZoneHelper.UtcNow,
            scheduledStart = classSession.Scheduledstart,
            scheduledEnd = classSession.Scheduledend,
            lobbyMinimumMinutes = _settings.LobbyPresenceMinimumMinutes,
            presenceRoles,
            admissionRoles,
            qualifyingLobbyRoles,
            lobbyVisits = lobbyVisits.Select(v => new
            {
                v.Role,
                v.EnteredAt,
                v.LastSeenAt,
                minutes = Math.Round((v.LastSeenAt - v.EnteredAt).TotalMinutes, 2)
            })
        };

        return new SessionAttendance(tutorAttended, learnerAttended, evidence);
    }

    private sealed record LobbyVisitWindow(string Role, DateTime EnteredAt, DateTime LastSeenAt);

    private sealed record SessionAttendance(bool TutorAttended, bool LearnerAttended, object Evidence)
    {
        public bool AnyoneAttended => TutorAttended || LearnerAttended;
    }

    // ── Thông báo ────────────────────────────────────────────────────────────

    private static IEnumerable<string> ResolveNotifyTargets(ClassSession classSession)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var candidate in new[]
                 {
                     classSession.Tutorid,
                     classSession.Booking?.Student?.Linkeduserid,
                     classSession.Booking?.Student?.Parentid ?? classSession.Booking?.Parentid
                 })
        {
            if (!string.IsNullOrWhiteSpace(candidate) && seen.Add(candidate))
                yield return candidate;
        }
    }

    /// <summary>
    /// Job chạy lặp nên phải chống gửi trùng — dùng đúng cách ClassSessionReminderJob đã dùng
    /// (tra theo user + type + reference) thay vì thêm cột đánh dấu vào class_sessions.
    /// </summary>
    private async Task NotifyOnceAsync(string userId, int classSessionId, string title, string message)
    {
        try
        {
            var alreadySent = await _notificationRepo.ExistsByUserAndTypeAndReferenceAsync(
                userId, NotificationType.LessonNoAttendance, classSessionId.ToString());
            if (alreadySent) return;

            await _notificationService.CreateNotificationAsync(new NotificationRequest
            {
                Userid = userId,
                Title = title,
                Message = message,
                Type = NotificationType.LessonNoAttendance,
                Referenceid = classSessionId.ToString()
            });
        }
        catch (Exception ex)
        {
            // Thông báo hỏng không được làm hỏng việc mở cửa sổ xác nhận đã commit ở trên.
            _logger.LogWarning(ex,
                "Không gửi được thông báo buổi học không có ai tham dự cho {UserId}, buổi {ClassSessionId}.",
                userId, classSessionId);
        }
    }
}
