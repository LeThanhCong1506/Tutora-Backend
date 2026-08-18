using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MV.ApplicationLayer.Hubs;
using MV.ApplicationLayer.Interfaces;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.Helpers;

namespace MV.ApplicationLayer.Services;

/// <inheritdoc cref="ISessionLobbyPresenceBroadcaster" />
public class SessionLobbyPresenceBroadcaster(
    IHubContext<SessionLobbyHub> hubContext,
    IAppDbContext db,
    ISessionPresenceService presence,
    IClassSessionScheduleChangeService scheduleChanges,
    ILogger<SessionLobbyPresenceBroadcaster> logger) : ISessionLobbyPresenceBroadcaster
{
    private static string LobbyGroup(int classSessionId) => $"lobby:{classSessionId}";

    public async Task BroadcastAsync(
        int classSessionId,
        string actorUserId,
        string? actorRole,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var session = await db.ClassSessions
                .AsNoTracking()
                .Where(l => l.Classsessionid == classSessionId)
                .Select(l => new
                {
                    TutorId = l.Tutorid,
                    StudentUserId = l.Booking != null && l.Booking.Student != null ? l.Booking.Student.Linkeduserid : null
                })
                .FirstOrDefaultAsync(cancellationToken);
            if (session == null) return;

            var tutorWaiting = !string.IsNullOrEmpty(session.TutorId)
                && (presence.IsInLobby(classSessionId, session.TutorId) || presence.IsPresent(classSessionId, session.TutorId));
            var studentWaiting = !string.IsNullOrEmpty(session.StudentUserId)
                && (presence.IsInLobby(classSessionId, session.StudentUserId!) || presence.IsPresent(classSessionId, session.StudentUserId!));

            var clients = hubContext.Clients.Group(LobbyGroup(classSessionId));

            // Đọc cổng dùng chung từ góc nhìn gia sư — giống SessionLobbyHub.BroadcastLobbyStateAsync:
            // Admin có thể bỏ qua xác nhận để hỗ trợ, nhưng admin mở lobby không được mở khoá hộ học viên.
            var stateActorId = session.TutorId ?? actorUserId;
            var stateActorRole = session.TutorId != null ? UserRole.Tutor : actorRole;
            var scheduleChange = await scheduleChanges.GetOrCreateStateAsync(
                classSessionId, stateActorId, stateActorRole, cancellationToken);

            await clients.SendAsync("scheduleChangeState", scheduleChange, cancellationToken);
            await clients.SendAsync("lobbyState", new { classSessionId, tutorWaiting, studentWaiting }, cancellationToken);

            if (tutorWaiting && studentWaiting && scheduleChange.AdmissionAllowed)
            {
                var conflict = await scheduleChanges.FindCurrentConflictAsync(
                    classSessionId, TimeZoneHelper.UtcNow, cancellationToken);
                if (conflict != null)
                {
                    await clients.SendAsync("scheduleConflict", conflict, cancellationToken);
                    return;
                }

                await clients.SendAsync("scheduleConflictCleared", new { classSessionId }, cancellationToken);
                await clients.SendAsync("sessionReady", new { classSessionId }, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            // Best-effort — presence tự phục hồi ở chu kỳ RefreshState/heartbeat kế tiếp nếu lần này lỗi.
            logger.LogWarning(ex, "Could not broadcast lobby presence for classSession {ClassSessionId}", classSessionId);
        }
    }
}
