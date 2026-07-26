using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.Common.Hubs;
using MV.ApplicationLayer.Interfaces;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using System.Security.Claims;

namespace MV.ApplicationLayer.Hubs
{
    /// <summary>
    /// Hub lobby (phòng chờ) trước khi vào lớp học online.
    ///
    /// Luồng: người dùng bấm "Vào lớp" → FE connect hub này và invoke <see cref="JoinLobby"/>.
    /// Người vào trước chờ trong lobby; khi cả hai phía (gia sư + học viên)
    /// cùng có mặt — trong lobby hoặc đã ở sẵn trong phòng
    /// học — server phát <c>sessionReady</c> cho cả group để FE tự chuyển mọi người vào phòng
    /// Agora của buổi học. Check-in KHÔNG xảy ra ở lobby: vẫn do heartbeat trong phòng đảm nhiệm
    /// (xem AgoraController.Heartbeat), đúng nghĩa "cả 2 vào được lớp mới điểm danh".
    ///
    /// Sự kiện gửi xuống client:
    ///  - lobbyInfo    { classSessionId, tutorName, studentName, scheduledStart, scheduledEnd }
    ///  - lobbyState   { classSessionId, tutorWaiting, studentWaiting }
    ///  - sessionReady { classSessionId }         → FE điều hướng sang phòng học
    ///  - lobbyClosed  { classSessionId, reason } → "ended" (đã kết thúc) | "unavailable"
    ///  - lobbyBlocked { classSessionId, reason } → "payment" (chưa thanh toán đợt 2)
    /// </summary>
    [Authorize]
    public class SessionLobbyHub : BaseHub
    {
        private readonly ILogger<SessionLobbyHub> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly ISessionPresenceService _presence;
        private readonly IClassSessionScheduleChangeService _scheduleChanges;

        public SessionLobbyHub(
            ILogger<SessionLobbyHub> logger,
            IServiceProvider serviceProvider,
            ISessionPresenceService presence,
            IClassSessionScheduleChangeService scheduleChanges) : base(logger, serviceProvider)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _presence = presence;
            _scheduleChanges = scheduleChanges;
        }

        private string? CurrentUserRole =>
            Context.User?.FindFirst("http://schemas.microsoft.com/ws/2008/06/identity/claims/role")?.Value
            ?? Context.User?.FindFirst(ClaimTypes.Role)?.Value;

        private static string LobbyGroup(int classSessionId) => $"lobby:{classSessionId}";

        /// <summary>
        /// Vào phòng chờ của một buổi học. Chỉ gia sư, học viên thuộc booking
        /// (hoặc Admin hỗ trợ) mới được vào; phụ huynh xác nhận ở trang chi tiết.
        /// </summary>
        public async Task JoinLobby(int classSessionId)
        {
            var userId = CurrentUserId;
            if (string.IsNullOrEmpty(userId))
                throw new HubException("User not authenticated");

            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();

            var session = await LoadSessionAsync(db, classSessionId)
                ?? throw new HubException("Không tìm thấy buổi học.");

            if (!HasAccess(session, userId))
                throw new HubException("Bạn không có quyền tham gia buổi học này.");

            // Buổi đã check-out → phòng đã đóng vĩnh viễn cho hôm đó.
            if (session.CheckoutTime != null)
            {
                await Clients.Caller.SendAsync("lobbyClosed", new { classSessionId, reason = "ended" });
                return;
            }

            // Đang diễn ra (đã check-in) → không cần chờ: cho vào lại phòng ngay.
            // Đây là đường "rớt mạng vào lại" — chỉ bị chặn khi gia sư đã kết thúc buổi.
            if (session.Status == ClassSessionStatus.InProgress)
            {
                await Clients.Caller.SendAsync("sessionReady", new { classSessionId });
                return;
            }

            // Các trạng thái khác scheduled (reserved, completed, cancelled, ...) → không mở lobby.
            if (session.Status != ClassSessionStatus.Scheduled)
            {
                await Clients.Caller.SendAsync("lobbyClosed", new { classSessionId, reason = "unavailable" });
                return;
            }

            // Rào thanh toán đợt 2 — giống điều kiện cấp token phòng học: chặn từ lobby để
            // hai bên không phải chờ một buổi không thể bắt đầu.
            var classSessionService = scope.ServiceProvider.GetRequiredService<IClassSessionService>();
            if (CurrentUserRole != UserRole.Admin
                && await classSessionService.IsSessionBlockedByRemainingPaymentAsync(classSessionId))
            {
                await Clients.Caller.SendAsync("lobbyBlocked", new { classSessionId, reason = "payment" });
                return;
            }

            _presence.JoinLobby(classSessionId, userId, Context.ConnectionId);
            await Groups.AddToGroupAsync(Context.ConnectionId, LobbyGroup(classSessionId));

            var participantRole = ResolveParticipantRole(session, userId);
            if (participantRole != SessionParticipantRole.Unknown)
            {
                var sessionLogService = scope.ServiceProvider.GetRequiredService<ISessionLogService>();
                await sessionLogService.RecordLobbyJoinAsync(
                    classSessionId,
                    userId,
                    participantRole,
                    Context.ConnectionId);
            }

            _logger.LogInformation("User {UserId} joined lobby of classSession {ClassSessionId}", userId, classSessionId);

            await Clients.Caller.SendAsync("lobbyInfo", new
            {
                classSessionId,
                tutorName = session.TutorName,
                studentName = session.StudentName,
                scheduledStart = session.ScheduledStart,
                scheduledEnd = session.ScheduledEnd
            });

            await BroadcastLobbyStateAsync(classSessionId, session, userId);
        }

        /// <summary>Rời phòng chờ chủ động (FE gọi khi unmount trang lobby).</summary>
        public async Task LeaveLobby()
        {
            var entry = _presence.RemoveLobbyConnection(Context.ConnectionId);
            if (entry == null) return;

            await Groups.RemoveFromGroupAsync(Context.ConnectionId, LobbyGroup(entry.Value.ClassSessionId));
            await CloseLobbyEvidenceAsync(
                entry.Value.ClassSessionId,
                entry.Value.UserId,
                Context.ConnectionId,
                SessionLobbyVisitCloseReason.Leave);
            await NotifyLobbyAfterExitAsync(entry.Value.ClassSessionId, entry.Value.UserId);
        }

        /// <summary>
        /// FE gọi định kỳ (~10s) khi đang chờ — lưới an toàn cho trường hợp phía kia vào thẳng
        /// phòng học không qua lobby (deep-link/tab cũ): presence phòng chỉ được ghi qua REST
        /// heartbeat nên không tự phát sự kiện tới lobby. Đồng thời tự vá các broadcast bị lỡ.
        /// </summary>
        public async Task RefreshState()
        {
            var userId = CurrentUserId;
            if (string.IsNullOrEmpty(userId)) return;

            // Chỉ có tác dụng khi connection này đang thực sự chờ trong một lobby.
            var entry = _presence.GetLobbyEntry(Context.ConnectionId);
            if (entry == null) return;

            var classSessionId = entry.Value.ClassSessionId;

            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            var session = await LoadSessionAsync(db, classSessionId);
            if (session == null) return;

            var sessionLogService = scope.ServiceProvider.GetRequiredService<ISessionLogService>();
            await sessionLogService.RecordLobbyHeartbeatAsync(
                classSessionId,
                userId,
                Context.ConnectionId);

            // Buổi có thể đã đổi trạng thái trong lúc chờ (vd. bị hủy, hoặc đã check-in ở nơi khác).
            if (session.CheckoutTime != null)
            {
                await Clients.Caller.SendAsync("lobbyClosed", new { classSessionId, reason = "ended" });
                return;
            }
            if (session.Status == ClassSessionStatus.InProgress)
            {
                await Clients.Caller.SendAsync("sessionReady", new { classSessionId });
                return;
            }
            if (session.Status != ClassSessionStatus.Scheduled)
            {
                await Clients.Caller.SendAsync("lobbyClosed", new { classSessionId, reason = "unavailable" });
                return;
            }

            await BroadcastLobbyStateAsync(classSessionId, session, userId);
        }

        /// <summary>Gia sư hoặc phía người học xác nhận/từ chối học ngoài lịch dự kiến.</summary>
        public async Task RespondToScheduleChange(int classSessionId, bool confirmed)
        {
            var userId = CurrentUserId;
            if (string.IsNullOrEmpty(userId))
                throw new HubException("User not authenticated");

            var entry = _presence.GetLobbyEntry(Context.ConnectionId);
            if (entry == null || entry.Value.ClassSessionId != classSessionId)
                throw new HubException("Bạn cần ở trong phòng chờ để xác nhận đổi lịch.");

            try
            {
                var state = await _scheduleChanges.RespondAsync(
                    classSessionId,
                    userId,
                    CurrentUserRole,
                    confirmed,
                    Context.ConnectionAborted);
                await Clients.Group(LobbyGroup(classSessionId)).SendAsync("scheduleChangeState", state);

                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
                var session = await LoadSessionAsync(db, classSessionId);
                if (session != null)
                    await BroadcastLobbyStateAsync(classSessionId, session, userId);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new HubException(ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                throw new HubException(ex.Message);
            }
        }
        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var entry = _presence.RemoveLobbyConnection(Context.ConnectionId);
            if (entry != null)
            {
                // Group membership tự mất khi disconnect — chỉ cần báo cho người còn lại.
                await CloseLobbyEvidenceAsync(
                    entry.Value.ClassSessionId,
                    entry.Value.UserId,
                    Context.ConnectionId,
                    SessionLobbyVisitCloseReason.Disconnect);
                await NotifyLobbyAfterExitAsync(entry.Value.ClassSessionId, entry.Value.UserId);
            }

            await base.OnDisconnectedAsync(exception);
        }

        // ── Private helpers ──────────────────────────────────────────────────

        /// <summary>Ảnh chụp gọn các thông tin của buổi học mà lobby cần.</summary>
        private sealed record LobbySessionSnapshot(
            string? TutorId,
            string? ParentId,
            string? StudentUserId,
            string? Status,
            DateTime? CheckoutTime,
            DateTime ScheduledStart,
            DateTime ScheduledEnd,
            string TutorName,
            string StudentName);

        private static async Task<LobbySessionSnapshot?> LoadSessionAsync(IAppDbContext db, int classSessionId)
        {
            return await db.ClassSessions
                .AsNoTracking()
                .Where(l => l.Classsessionid == classSessionId)
                .Select(l => new LobbySessionSnapshot(
                    l.Tutorid,
                    l.Booking != null ? l.Booking.Parentid : null,
                    l.Booking != null && l.Booking.Student != null ? l.Booking.Student.Linkeduserid : null,
                    l.Status,
                    l.Checkouttime,
                    l.Scheduledstart,
                    l.Scheduledend,
                    l.Tutor != null && l.Tutor.Tutor != null && l.Tutor.Tutor.Fullname != null ? l.Tutor.Tutor.Fullname : "Gia sư",
                    l.Booking != null && l.Booking.Student != null && l.Booking.Student.Fullname != null ? l.Booking.Student.Fullname : "Học sinh"))
                .FirstOrDefaultAsync();
        }

        /// <summary>Cùng quy tắc truy cập với AgoraController: Admin, gia sư hoặc học viên của booking.</summary>
        private bool HasAccess(LobbySessionSnapshot session, string userId)
        {
            if (CurrentUserRole == UserRole.Admin) return true;
            if (session.TutorId == userId) return true;
            if (!string.IsNullOrEmpty(session.StudentUserId) && session.StudentUserId == userId) return true;
            return false;
        }

        private static string ResolveParticipantRole(LobbySessionSnapshot session, string userId)
        {
            if (session.TutorId == userId) return SessionParticipantRole.Tutor;
            if (session.StudentUserId == userId) return SessionParticipantRole.Student;
            if (session.ParentId == userId) return SessionParticipantRole.Parent;
            return SessionParticipantRole.Unknown;
        }

        /// <summary>
        /// "Có mặt" = đang chờ trong lobby HOẶC đã ở trong phòng học (presence heartbeat).
        /// Phía học viên chỉ tính tài khoản student (Linkeduserid). Phụ huynh không
        /// được thay thế học viên trong lobby hoặc phòng gọi.
        /// </summary>
        private (bool TutorWaiting, bool StudentWaiting) ComputeState(int classSessionId, LobbySessionSnapshot session)
        {
            var tutorWaiting = !string.IsNullOrEmpty(session.TutorId)
                && (_presence.IsInLobby(classSessionId, session.TutorId) || _presence.IsPresent(classSessionId, session.TutorId));

            bool studentWaiting;
            if (!string.IsNullOrEmpty(session.StudentUserId))
            {
                studentWaiting = _presence.IsInLobby(classSessionId, session.StudentUserId)
                    || _presence.IsPresent(classSessionId, session.StudentUserId);
            }
            else
            {
                studentWaiting = false;
            }

            return (tutorWaiting, studentWaiting);
        }

        private async Task BroadcastLobbyStateAsync(
            int classSessionId,
            LobbySessionSnapshot session,
            string actorUserId)
        {
            var (tutorWaiting, studentWaiting) = ComputeState(classSessionId, session);
            // Read the shared gate from the tutor perspective. Admin may bypass admission for
            // support, but an admin opening the lobby must never unlock it for participants.
            var stateActorId = session.TutorId ?? actorUserId;
            var stateActorRole = session.TutorId != null ? UserRole.Tutor : CurrentUserRole;
            var scheduleChange = await _scheduleChanges.GetOrCreateStateAsync(
                classSessionId,
                stateActorId,
                stateActorRole,
                Context.ConnectionAborted);

            await Clients.Group(LobbyGroup(classSessionId)).SendAsync("scheduleChangeState", scheduleChange);
            await Clients.Group(LobbyGroup(classSessionId)).SendAsync("lobbyState", new
            {
                classSessionId,
                tutorWaiting,
                studentWaiting
            });

            if (tutorWaiting && studentWaiting && scheduleChange.AdmissionAllowed)
            {
                _logger.LogInformation(
                    "Lobby of classSession {ClassSessionId} ready — presence and schedule confirmations are complete",
                    classSessionId);
                await Clients.Group(LobbyGroup(classSessionId)).SendAsync("sessionReady", new { classSessionId });
            }
        }
        /// <summary>Sau khi một người rời lobby: cập nhật trạng thái chờ cho những người còn lại.</summary>
        private async Task NotifyLobbyAfterExitAsync(int classSessionId, string userId)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
                var session = await LoadSessionAsync(db, classSessionId);
                if (session == null) return;

                _logger.LogInformation("User {UserId} left lobby of classSession {ClassSessionId}", userId, classSessionId);
                await BroadcastLobbyStateAsync(classSessionId, session, userId);
            }
            catch (Exception ex)
            {
                // Best-effort: lỗi cập nhật trạng thái cho người còn lại không được làm hỏng disconnect.
                _logger.LogWarning(ex, "Failed to broadcast lobby state after user {UserId} left classSession {ClassSessionId}", userId, classSessionId);
            }
        }

        private async Task CloseLobbyEvidenceAsync(
            int classSessionId,
            string userId,
            string connectionId,
            string closedReason)
        {
            using var scope = _serviceProvider.CreateScope();
            var sessionLogService = scope.ServiceProvider.GetRequiredService<ISessionLogService>();
            await sessionLogService.CloseLobbyVisitAsync(
                classSessionId,
                userId,
                connectionId,
                closedReason);
        }
    }
}
