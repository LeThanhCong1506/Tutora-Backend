using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MV.ApplicationLayer.Interfaces;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.Configuration;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Entities;
using MV.DomainLayer.Helpers;
using System.Globalization;
using System.Text.Json;
using Interval = MV.DomainLayer.DTO.ResponseModel.SessionLogInterval;

namespace MV.ApplicationLayer.Services;

/// <summary>
/// Turns raw Agora channel events into an attendance record staff can act on.
///
/// Agora reports a channel <c>uid</c> that carries no application meaning: the lesson room is
/// joined with a string user account and the notification payload may carry Agora's internal
/// numeric id instead. Identity is therefore resolved by exact account, direct uid, and finally a
/// time correlation against <c>session_participants</c>. Every participant carries the confidence
/// of its binding so staff never mistake a guess for a fact.
///
/// Four independent sources are combined, and reported separately rather than merged:
/// Agora's server-side channel events (strongest), the heartbeat chain from our own classroom
/// client (the only source that can tell a room left open apart from a lesson being taught), lobby
/// visits captured before room admission, and the network/device each admission came from (the
/// account-sharing signal). When two sources disagree, that disagreement is the finding, so
/// neither is allowed to overwrite the other.
/// </summary>
public class SessionLogService(
    IAppDbContext context,
    ILogger<SessionLogService> logger,
    IOptions<AgoraRecordingSettings> recordingSettings,
    IOptions<SessionEvidenceSettings> evidenceSettings) : ISessionLogService
{
    private const string RecorderDisplayName = "Máy ghi hình Agora";

    /// <summary>Cap on rows returned by the reliability report, so one query cannot fetch a year.</summary>
    private const int MaxReliabilitySessions = 500;

    private readonly string? recorderUid = recordingSettings.Value.RecorderUid == 0
        ? null
        : recordingSettings.Value.RecorderUid.ToString(CultureInfo.InvariantCulture);

    private readonly SessionEvidenceSettings evidence = evidenceSettings.Value;

    /// <summary>
    /// How far apart an admission and the matching Agora join may sit before we refuse to bind
    /// them. Admission always precedes the join (the client needs the token first); five minutes
    /// covers a slow device without pairing two people who joined at unrelated times.
    /// </summary>
    private static readonly TimeSpan CorrelationWindow = TimeSpan.FromMinutes(5);

    /// <summary>Clock skew allowance for an Agora join timestamped just before our admission row.</summary>
    private static readonly TimeSpan CorrelationSkew = TimeSpan.FromSeconds(30);

    /// <inheritdoc/>
    public async Task RecordLobbyJoinAsync(
        int classSessionId,
        string appUserId,
        string role,
        string connectionId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(appUserId) || string.IsNullOrWhiteSpace(connectionId))
            return;

        try
        {
            var now = TimeZoneHelper.UtcNow;
            var normalizedConnectionId = Clip(connectionId, 128);
            var existing = await context.SessionLobbyVisits
                .FirstOrDefaultAsync(
                    visit => visit.ClassSessionId == classSessionId
                             && visit.ConnectionId == normalizedConnectionId,
                    cancellationToken);

            if (existing == null)
            {
                context.SessionLobbyVisits.Add(new SessionLobbyVisit
                {
                    ClassSessionId = classSessionId,
                    AppUserId = appUserId,
                    Role = role,
                    ConnectionId = normalizedConnectionId,
                    EnteredAt = now,
                    LastSeenAt = now,
                    BeatCount = 1
                });
            }
            else
            {
                // JoinLobby can be invoked again on the same still-connected transport after a
                // client retry. Keep one visit for that connection and reopen it if necessary.
                existing.LastSeenAt = now;
                existing.BeatCount += 1;
                existing.LeftAt = null;
                existing.ClosedReason = null;
                if (existing.Role == SessionParticipantRole.Unknown && role != SessionParticipantRole.Unknown)
                    existing.Role = role;
            }

            await context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Could not record lobby join for user {UserId} in class session {ClassSessionId}.",
                appUserId,
                classSessionId);
        }
    }

    /// <inheritdoc/>
    public async Task RecordLobbyHeartbeatAsync(
        int classSessionId,
        string appUserId,
        string connectionId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(appUserId) || string.IsNullOrWhiteSpace(connectionId))
            return;

        try
        {
            var normalizedConnectionId = Clip(connectionId, 128);
            var visit = await context.SessionLobbyVisits
                .FirstOrDefaultAsync(
                    row => row.ClassSessionId == classSessionId
                           && row.AppUserId == appUserId
                           && row.ConnectionId == normalizedConnectionId
                           && row.ClosedReason == null,
                    cancellationToken);

            if (visit == null)
                return;

            visit.LastSeenAt = TimeZoneHelper.UtcNow;
            visit.BeatCount += 1;
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Could not record lobby heartbeat for user {UserId} in class session {ClassSessionId}.",
                appUserId,
                classSessionId);
        }
    }

    /// <inheritdoc/>
    public async Task CloseLobbyVisitAsync(
        int classSessionId,
        string appUserId,
        string connectionId,
        string closedReason,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(appUserId) || string.IsNullOrWhiteSpace(connectionId))
            return;

        try
        {
            var normalizedConnectionId = Clip(connectionId, 128);
            var visit = await context.SessionLobbyVisits
                .FirstOrDefaultAsync(
                    row => row.ClassSessionId == classSessionId
                           && row.AppUserId == appUserId
                           && row.ConnectionId == normalizedConnectionId
                           && row.ClosedReason == null,
                    cancellationToken);

            if (visit == null)
                return;

            var now = TimeZoneHelper.UtcNow;
            if (now > visit.LastSeenAt)
                visit.LastSeenAt = now;
            visit.LeftAt = now;
            visit.ClosedReason = Clip(closedReason, 20);
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Could not close lobby visit for user {UserId} in class session {ClassSessionId}.",
                appUserId,
                classSessionId);
        }
    }

    /// <inheritdoc/>
    public async Task RecordAdmissionAsync(
        int classSessionId,
        string appUserId,
        string role,
        SessionAdmissionContext? admissionContext = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(appUserId))
            return;

        try
        {
            var now = TimeZoneHelper.UtcNow;
            var existing = await context.SessionParticipants
                .FirstOrDefaultAsync(
                    p => p.ClassSessionId == classSessionId && p.AppUserId == appUserId,
                    cancellationToken);

            if (existing == null)
            {
                context.SessionParticipants.Add(new SessionParticipant
                {
                    ClassSessionId = classSessionId,
                    AppUserId = appUserId,
                    Role = role,
                    FirstAdmittedAt = now,
                    LastAdmittedAt = now,
                    AdmissionCount = 1
                });
            }
            else
            {
                // Token renewal re-enters admission every couple of minutes; only the first one
                // is the real arrival, so FirstAdmittedAt must never move.
                existing.LastAdmittedAt = now;
                existing.AdmissionCount += 1;
                if (existing.Role == SessionParticipantRole.Unknown && role != SessionParticipantRole.Unknown)
                    existing.Role = role;
            }

            await context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // Evidence bookkeeping must never cost a student their lesson.
            logger.LogWarning(
                ex,
                "Could not record session admission for user {UserId} in class session {ClassSessionId}.",
                appUserId,
                classSessionId);
        }

        if (admissionContext == null)
            return;

        // Saved separately from the admission row above. Two devices admitting at the same instant
        // can collide on the device table's unique key, and that must not take the identity row —
        // which is what binds Agora uids to real users — down with it.
        try
        {
            await RecordAdmissionDeviceAsync(
                classSessionId,
                appUserId,
                role,
                admissionContext,
                TimeZoneHelper.UtcNow,
                cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Could not record admission device for user {UserId} in class session {ClassSessionId}.",
                appUserId,
                classSessionId);
        }
    }

    /// <summary>
    /// Upserts the (network, device) pair this admission arrived from. A returning device only
    /// bumps its counters, so a 90-minute lesson of token renewals stays one row and a genuine
    /// second device stays visible as a second row.
    /// </summary>
    private async Task RecordAdmissionDeviceAsync(
        int classSessionId,
        string appUserId,
        string role,
        SessionAdmissionContext admissionContext,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var ipAddress = Clip(admissionContext.IpAddress, 45);
        var deviceId = Clip(admissionContext.DeviceId, 100);

        var existing = await context.SessionParticipantDevices
            .FirstOrDefaultAsync(
                d => d.ClassSessionId == classSessionId
                     && d.AppUserId == appUserId
                     && d.IpAddress == ipAddress
                     && d.DeviceId == deviceId,
                cancellationToken);

        if (existing == null)
        {
            context.SessionParticipantDevices.Add(new SessionParticipantDevice
            {
                ClassSessionId = classSessionId,
                AppUserId = appUserId,
                Role = role,
                IpAddress = ipAddress,
                DeviceId = deviceId,
                DeviceLabel = Clip(admissionContext.DeviceLabel, 120),
                UserAgent = Clip(admissionContext.UserAgent, 400),
                FirstSeenAt = now,
                LastSeenAt = now,
                AdmissionCount = 1
            });
            return;
        }

        existing.LastSeenAt = now;
        existing.AdmissionCount += 1;
        if (existing.Role == SessionParticipantRole.Unknown && role != SessionParticipantRole.Unknown)
            existing.Role = role;
    }

    /// <inheritdoc/>
    public async Task RecordHeartbeatAsync(
        int classSessionId,
        string appUserId,
        string role,
        LiveSessionActivityReport? activity,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(appUserId))
            return;

        try
        {
            var now = TimeZoneHelper.UtcNow;
            var latest = await LatestRunAsync(classSessionId, appUserId, cancellationToken);
            var maxGap = TimeSpan.FromSeconds(Math.Max(1, evidence.HeartbeatGapSeconds));

            if (latest is { ClosedReason: null } && now - latest.LastBeatAt <= maxGap)
            {
                latest.BeatCount += 1;
                // Requests can arrive out of order; the run's end must only ever move forward.
                if (now > latest.LastBeatAt)
                    latest.LastBeatAt = now;
                ApplyActivity(latest, activity);
            }
            else
            {
                // Beats stopped for longer than the window, so the previous run genuinely ended.
                // Recording that as a gap is what makes "they went quiet mid-lesson" visible.
                if (latest is { ClosedReason: null })
                    latest.ClosedReason = PresenceIntervalCloseReason.Gap;

                var run = new SessionPresenceInterval
                {
                    ClassSessionId = classSessionId,
                    AppUserId = appUserId,
                    Role = role,
                    StartedAt = now,
                    LastBeatAt = now,
                    BeatCount = 1
                };
                ApplyActivity(run, activity);
                context.SessionPresenceIntervals.Add(run);
            }

            await context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Could not record presence heartbeat for user {UserId} in class session {ClassSessionId}.",
                appUserId,
                classSessionId);
        }
    }

    /// <inheritdoc/>
    public async Task CloseHeartbeatAsync(
        int classSessionId,
        string appUserId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(appUserId))
            return;

        try
        {
            var latest = await LatestRunAsync(classSessionId, appUserId, cancellationToken);
            if (latest is not { ClosedReason: null })
                return;

            latest.ClosedReason = PresenceIntervalCloseReason.Leave;
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Could not close presence heartbeat for user {UserId} in class session {ClassSessionId}.",
                appUserId,
                classSessionId);
        }
    }

    private Task<SessionPresenceInterval?> LatestRunAsync(
        int classSessionId,
        string appUserId,
        CancellationToken cancellationToken)
    {
        return context.SessionPresenceIntervals
            .Where(i => i.ClassSessionId == classSessionId && i.AppUserId == appUserId)
            .OrderByDescending(i => i.LastBeatAt)
            .ThenByDescending(i => i.IntervalId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// Folds one beat's client-reported state into its run. <c>IdleBeats</c> counts the conjunction
    /// — no microphone, no camera, and an idle tab — because that combination is the "room left
    /// open" signal, while a muted camera on its own is ordinary. Beats without an activity report
    /// leave every counter alone, so absent data can never be read as absent activity.
    /// </summary>
    private static void ApplyActivity(SessionPresenceInterval run, LiveSessionActivityReport? activity)
    {
        if (activity == null)
            return;

        run.ReportedBeats += 1;
        if (activity.MicOn) run.MicOnBeats += 1;
        if (activity.CameraOn) run.CameraOnBeats += 1;
        if (activity.Idle && !activity.MicOn && !activity.CameraOn) run.IdleBeats += 1;
    }

    private static string Clip(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    /// <inheritdoc/>
    public async Task<SessionLogResponse?> GetSessionLogAsync(
        int classSessionId,
        CancellationToken cancellationToken = default)
    {
        var classSession = await context.ClassSessions
            .AsNoTracking()
            .Include(s => s.Tutor!).ThenInclude(t => t.Tutor)
            .Include(s => s.Booking!).ThenInclude(b => b.Student)
            .Include(s => s.Booking!).ThenInclude(b => b.Parent)
            .FirstOrDefaultAsync(s => s.Classsessionid == classSessionId, cancellationToken);

        if (classSession == null)
            return null;

        var events = await context.AgoraChannelEvents
            .AsNoTracking()
            .Where(e => e.ClassSessionId == classSessionId)
            .OrderBy(e => e.EventAt)
            .ThenBy(e => e.EventId)
            .ToListAsync(cancellationToken);

        var registry = await context.SessionParticipants
            .AsNoTracking()
            .Where(p => p.ClassSessionId == classSessionId)
            .OrderBy(p => p.FirstAdmittedAt)
            .ToListAsync(cancellationToken);

        var runs = await context.SessionPresenceIntervals
            .AsNoTracking()
            .Where(i => i.ClassSessionId == classSessionId)
            .OrderBy(i => i.StartedAt)
            .ThenBy(i => i.IntervalId)
            .ToListAsync(cancellationToken);

        var devices = await context.SessionParticipantDevices
            .AsNoTracking()
            .Where(d => d.ClassSessionId == classSessionId)
            .OrderBy(d => d.FirstSeenAt)
            .ToListAsync(cancellationToken);

        var lobbyVisits = await context.SessionLobbyVisits
            .AsNoTracking()
            .Where(visit => visit.ClassSessionId == classSessionId)
            .OrderBy(visit => visit.EnteredAt)
            .ThenBy(visit => visit.LobbyVisitId)
            .ToListAsync(cancellationToken);

        return BuildLog(classSession, events, registry, runs, devices, lobbyVisits);
    }

    /// <summary>
    /// Assembles one lesson's report from data already loaded. Kept separate from the query so the
    /// reliability report can fetch a whole date range in a handful of round trips and reuse the
    /// exact same reasoning per session — a rate that disagreed with the log it summarises would be
    /// worse than no rate at all.
    /// </summary>
    private SessionLogResponse BuildLog(
        ClassSession classSession,
        List<AgoraChannelEvent> events,
        List<SessionParticipant> registry,
        List<SessionPresenceInterval> runs,
        List<SessionParticipantDevice> devices,
        List<SessionLobbyVisit> lobbyVisits)
    {
        var classSessionId = classSession.Classsessionid;
        var flags = new HashSet<string>();
        var known = BuildKnownParticipants(classSession, registry, flags);
        var parsed = ParseEvents(events, flags);
        var recorderUids = FindRecorderUids(parsed);
        if (recorderUids.Count > 0)
            flags.Add(SessionLogFlag.RecorderPresent);
        var bindings = ResolveIdentities(parsed, known, recorderUids, flags);

        var snapshotAt = TimeZoneHelper.UtcNow;
        var isOngoing = IsOngoing(classSession);
        var sessionEnd = DetermineSessionEnd(parsed, classSession, snapshotAt, isOngoing);
        var participants = BuildParticipants(
            parsed,
            bindings,
            known,
            recorderUids,
            sessionEnd,
            isOngoing,
            flags);
        var heartbeats = BuildHeartbeats(runs, known, snapshotAt, isOngoing, flags);
        var deviceUses = BuildDeviceUses(devices, known, flags);
        var lobby = BuildLobbyEvidence(
            classSession,
            lobbyVisits,
            registry,
            known,
            snapshotAt,
            isOngoing);
        var summary = BuildSummary(
            classSession,
            parsed,
            participants,
            heartbeats,
            snapshotAt,
            isOngoing,
            flags);
        var timeline = BuildTimeline(parsed, bindings, recorderUids);

        if (events.Count == 0)
            flags.Add(SessionLogFlag.NoAgoraData);

        return new SessionLogResponse
        {
            ClassSessionId = classSessionId,
            Summary = summary,
            Participants = participants,
            Timeline = timeline,
            Heartbeats = heartbeats,
            Devices = deviceUses,
            Lobby = lobby,
            Flags = [.. flags]
        };
    }

    // ── Tutor reliability ─────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<TutorReliabilityResponse> GetTutorReliabilityAsync(
        string tutorUserId,
        DateTime fromDate,
        DateTime toDate,
        CancellationToken cancellationToken = default)
    {
        var response = new TutorReliabilityResponse
        {
            TutorUserId = tutorUserId,
            FromDate = fromDate,
            ToDate = toDate
        };

        if (string.IsNullOrWhiteSpace(tutorUserId))
            return response;

        // A cancelled lesson cannot be attended, so counting one as a no-show would punish a tutor
        // for a booking that was called off.
        var sessions = await context.ClassSessions
            .AsNoTracking()
            .Include(s => s.Tutor!).ThenInclude(t => t.Tutor)
            .Include(s => s.Booking!).ThenInclude(b => b.Student)
            .Include(s => s.Booking!).ThenInclude(b => b.Parent)
            .Where(s => s.Tutorid == tutorUserId
                        && s.Scheduledstart >= fromDate
                        && s.Scheduledstart < toDate
                        && s.Status != ClassSessionStatus.Cancelled
                        && s.Status != ClassSessionStatus.CancelledNoshow)
            .OrderByDescending(s => s.Scheduledstart)
            .Take(MaxReliabilitySessions)
            .ToListAsync(cancellationToken);

        response.TutorName = sessions
            .Select(s => s.Tutor?.Tutor?.Fullname)
            .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name));
        response.SessionsInRange = sessions.Count;

        if (sessions.Count == 0)
            return response;

        // Three batched reads instead of three per lesson; each list is then sliced in memory.
        var sessionIds = sessions.Select(s => s.Classsessionid).ToList();

        var eventsBySession = (await context.AgoraChannelEvents
                .AsNoTracking()
                .Where(e => e.ClassSessionId != null && sessionIds.Contains(e.ClassSessionId.Value))
                .OrderBy(e => e.EventAt)
                .ThenBy(e => e.EventId)
                .ToListAsync(cancellationToken))
            .GroupBy(e => e.ClassSessionId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        var registryBySession = (await context.SessionParticipants
                .AsNoTracking()
                .Where(p => sessionIds.Contains(p.ClassSessionId))
                .OrderBy(p => p.FirstAdmittedAt)
                .ToListAsync(cancellationToken))
            .GroupBy(p => p.ClassSessionId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var runsBySession = (await context.SessionPresenceIntervals
                .AsNoTracking()
                .Where(i => sessionIds.Contains(i.ClassSessionId))
                .OrderBy(i => i.StartedAt)
                .ThenBy(i => i.IntervalId)
                .ToListAsync(cancellationToken))
            .GroupBy(i => i.ClassSessionId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var devicesBySession = (await context.SessionParticipantDevices
                .AsNoTracking()
                .Where(d => sessionIds.Contains(d.ClassSessionId))
                .OrderBy(d => d.FirstSeenAt)
                .ToListAsync(cancellationToken))
            .GroupBy(d => d.ClassSessionId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var lateSamples = new List<int>();
        var earlyLeaveSamples = new List<int>();
        var overlapSamples = new List<double>();

        foreach (var session in sessions)
        {
            var id = session.Classsessionid;
            var log = BuildLog(
                session,
                eventsBySession.GetValueOrDefault(id, []),
                registryBySession.GetValueOrDefault(id, []),
                runsBySession.GetValueOrDefault(id, []),
                devicesBySession.GetValueOrDefault(id, []),
                []);

            var summary = log.Summary;
            var isNoShow = summary.IsEvidenceConclusive && log.Flags.Contains(SessionLogFlag.TutorNeverJoined);

            // A lesson counts towards the rates when its timing is usable, or when the evidence was
            // conclusive enough to say nobody came. Anything else is left out entirely: a stretch of
            // missing notifications must not read as a perfect record.
            var isMeasured = summary.PunctualitySource != null || isNoShow;

            var row = new TutorReliabilitySession
            {
                ClassSessionId = id,
                ScheduledStart = session.Scheduledstart,
                ScheduledEnd = session.Scheduledend,
                Status = session.Status,
                LateSeconds = summary.TutorLateSeconds,
                EarlyLeaveSeconds = summary.TutorEarlyLeaveSeconds,
                OverlapSeconds = summary.OverlapSeconds,
                OverlapRatio = summary.OverlapRatio,
                IsNoShow = isNoShow,
                IsMeasured = isMeasured,
                PunctualitySource = summary.PunctualitySource,
                Flags = log.Flags
            };
            response.Sessions.Add(row);

            if (log.Flags.Contains(SessionLogFlag.IdlePresence)) response.IdlePresenceCount += 1;
            if (log.Flags.Contains(SessionLogFlag.MultipleNetworks)) response.MultipleNetworkCount += 1;
            if (log.Flags.Contains(SessionLogFlag.MultipleDevices)) response.MultipleDeviceCount += 1;

            if (!isMeasured)
            {
                response.SessionsWithoutEvidence += 1;
                continue;
            }

            response.SessionsMeasured += 1;
            overlapSamples.Add(summary.OverlapRatio);

            if (isNoShow)
            {
                response.NoShowCount += 1;
                continue;
            }

            if (summary.TutorLateSeconds > 0)
            {
                response.LateCount += 1;
                lateSamples.Add(summary.TutorLateSeconds.Value);
            }

            // An interval we had to close ourselves says nothing reliable about when the tutor left:
            // the departure event may simply never have arrived. Lateness is unaffected — it comes
            // from the arrival — so only the early-leave rate skips these lessons.
            if (summary.TutorEarlyLeaveSeconds > 0
                && !log.Flags.Contains(SessionLogFlag.UnclosedInterval))
            {
                response.EarlyLeaveCount += 1;
                earlyLeaveSamples.Add(summary.TutorEarlyLeaveSeconds.Value);
            }
        }

        var measured = response.SessionsMeasured;
        if (measured > 0)
        {
            response.LateRate = Rate(response.LateCount, measured);
            response.EarlyLeaveRate = Rate(response.EarlyLeaveCount, measured);
            response.NoShowRate = Rate(response.NoShowCount, measured);
            response.AverageOverlapRatio = Math.Round(overlapSamples.Average(), 4);
        }

        if (lateSamples.Count > 0)
        {
            response.AverageLateSeconds = (int)Math.Round(lateSamples.Average());
            response.WorstLateSeconds = lateSamples.Max();
        }

        if (earlyLeaveSamples.Count > 0)
        {
            response.AverageEarlyLeaveSeconds = (int)Math.Round(earlyLeaveSamples.Average());
            response.WorstEarlyLeaveSeconds = earlyLeaveSamples.Max();
        }

        return response;
    }

    private static double Rate(int count, int total)
        => Math.Round(count / (double)total, 4);

    // ── Heartbeat chain ───────────────────────────────────────────────────────

    /// <summary>
    /// Reduces the stored beat runs into one entry per user.
    ///
    /// A run spans its first beat to its last, which undercounts by up to one beat period at the
    /// tail — deliberately, since the only thing we know is that the client was alive at each beat.
    /// Erring short keeps the number safe to use against a tutor's own claim.
    /// </summary>
    private List<SessionLogHeartbeat> BuildHeartbeats(
        List<SessionPresenceInterval> runs,
        List<KnownParticipant> known,
        DateTime snapshotAt,
        bool isOngoing,
        HashSet<string> flags)
    {
        if (runs.Count == 0)
            return [];

        var maxGap = TimeSpan.FromSeconds(Math.Max(1, evidence.HeartbeatGapSeconds));
        var byUser = known.ToDictionary(k => k.AppUserId, k => k, StringComparer.Ordinal);
        var result = new List<SessionLogHeartbeat>();

        foreach (var group in runs.GroupBy(r => r.AppUserId, StringComparer.Ordinal))
        {
            var ordered = CoalesceOverlappingRuns(
                [.. group.OrderBy(r => r.StartedAt).ThenBy(r => r.IntervalId)]);
            byUser.TryGetValue(group.Key, out var person);

            var reportedBeats = ordered.Sum(r => r.ReportedBeats);
            var idleBeats = ordered.Sum(r => r.IdleBeats);
            var last = ordered[^1];

            result.Add(new SessionLogHeartbeat
            {
                AppUserId = group.Key,
                // The run stores the role seen at beat time; the booking wins when it knows better.
                Role = person?.Role ?? ordered[0].Role,
                DisplayName = person?.DisplayName,
                FirstBeatAt = ordered[0].StartedAt,
                LastBeatAt = last.LastBeatAt,
                TotalSeconds = (int)ordered.Sum(r => (r.LastBeatAt - r.StartedAt).TotalSeconds),
                BeatCount = ordered.Sum(r => r.BeatCount),
                RunCount = ordered.Count,
                GapCount = ordered.Count(r => r.ClosedReason == PresenceIntervalCloseReason.Gap),
                IsCurrentlyBeating = isOngoing
                                     && last.ClosedReason == null
                                     && snapshotAt - last.LastBeatAt <= maxGap,
                ReportedBeats = reportedBeats,
                MicOnBeats = ordered.Sum(r => r.MicOnBeats),
                CameraOnBeats = ordered.Sum(r => r.CameraOnBeats),
                IdleBeats = idleBeats,
                IdleRatio = reportedBeats > 0
                    ? Math.Round(idleBeats / (double)reportedBeats, 4)
                    : null,
                Runs = [.. ordered.Select(r => new SessionLogHeartbeatRun
                {
                    StartedAt = r.StartedAt,
                    LastBeatAt = r.LastBeatAt,
                    BeatCount = r.BeatCount,
                    ReportedBeats = r.ReportedBeats,
                    MicOnBeats = r.MicOnBeats,
                    CameraOnBeats = r.CameraOnBeats,
                    IdleBeats = r.IdleBeats,
                    ClosedReason = r.ClosedReason,
                    ClosedReasonLabel = PresenceIntervalCloseReasonLabels.Label(r.ClosedReason)
                })]
            });
        }

        var humans = result.Where(h => IsHumanRole(h.Role)).ToList();

        if (humans.Count > 0 && humans.All(h => h.ReportedBeats == 0))
            flags.Add(SessionLogFlag.NoActivityReports);

        // An unattended room is only claimable once enough beats reported activity; below that a
        // mid-lesson reload would read as an absent tutor.
        if (humans.Any(h => h.ReportedBeats >= evidence.MinimumBeatsForIdleVerdict
                            && h.IdleRatio >= evidence.IdlePresenceThreshold))
        {
            flags.Add(SessionLogFlag.IdlePresence);
        }

        return [.. result.OrderBy(h => RoleOrder(h.Role)).ThenBy(h => h.FirstBeatAt ?? DateTime.MaxValue)];
    }

    /// <summary>
    /// Folds runs that overlap in time back into one.
    ///
    /// Two heartbeats racing at room entry can each open a run before either insert is visible to
    /// the other, leaving a stray one-beat twin beside the real run. That stray is a recording
    /// artifact, not a second presence, and reporting it as a separate run would overstate how
    /// often the connection broke. The tolerance is deliberately tiny: a real leave followed by a
    /// rejoin arrives tens of seconds apart and must remain two runs, because "left and came back"
    /// is exactly the story the chain exists to tell.
    /// </summary>
    private static List<SessionPresenceInterval> CoalesceOverlappingRuns(List<SessionPresenceInterval> ordered)
    {
        if (ordered.Count < 2)
            return ordered;

        var tolerance = TimeSpan.FromSeconds(2);
        var result = new List<SessionPresenceInterval> { CopyRun(ordered[0]) };

        foreach (var run in ordered.Skip(1))
        {
            var current = result[^1];
            if (run.StartedAt > current.LastBeatAt + tolerance)
            {
                result.Add(CopyRun(run));
                continue;
            }

            current.BeatCount += run.BeatCount;
            current.ReportedBeats += run.ReportedBeats;
            current.MicOnBeats += run.MicOnBeats;
            current.CameraOnBeats += run.CameraOnBeats;
            current.IdleBeats += run.IdleBeats;

            // Whichever twin kept beating longer holds the truth about how the run ended.
            if (run.LastBeatAt >= current.LastBeatAt)
            {
                current.LastBeatAt = run.LastBeatAt;
                current.ClosedReason = run.ClosedReason;
            }
        }

        return result;
    }

    /// <summary>Rows come from a no-tracking query, but the merge must still never edit its input.</summary>
    private static SessionPresenceInterval CopyRun(SessionPresenceInterval run) => new()
    {
        IntervalId = run.IntervalId,
        ClassSessionId = run.ClassSessionId,
        AppUserId = run.AppUserId,
        Role = run.Role,
        StartedAt = run.StartedAt,
        LastBeatAt = run.LastBeatAt,
        BeatCount = run.BeatCount,
        ReportedBeats = run.ReportedBeats,
        MicOnBeats = run.MicOnBeats,
        CameraOnBeats = run.CameraOnBeats,
        IdleBeats = run.IdleBeats,
        ClosedReason = run.ClosedReason
    };

    // ── Lobby evidence ────────────────────────────────────────────────────────

    /// <summary>
    /// Reduces one row per SignalR connection into one lobby account per user. The duration is the
    /// union of server-observed windows, so two tabs cannot double the claimed waiting time.
    /// </summary>
    private SessionLogLobbyEvidence BuildLobbyEvidence(
        ClassSession classSession,
        List<SessionLobbyVisit> visits,
        List<SessionParticipant> registry,
        List<KnownParticipant> known,
        DateTime snapshotAt,
        bool isOngoing)
    {
        if (visits.Count == 0)
            return new SessionLogLobbyEvidence();

        var byUser = known.ToDictionary(k => k.AppUserId, k => k, StringComparer.Ordinal);
        var admittedUsers = registry
            .Select(row => row.AppUserId)
            .ToHashSet(StringComparer.Ordinal);
        var freshness = TimeSpan.FromSeconds(Math.Max(1, evidence.LobbyHeartbeatGapSeconds));
        var participants = new List<SessionLogLobbyParticipant>();

        foreach (var group in visits.GroupBy(visit => visit.AppUserId, StringComparer.Ordinal))
        {
            var ordered = group
                .OrderBy(visit => visit.EnteredAt)
                .ThenBy(visit => visit.LobbyVisitId)
                .ToList();
            byUser.TryGetValue(group.Key, out var person);

            var windows = ordered
                .Select(visit =>
                {
                    var end = visit.LeftAt ?? visit.LastSeenAt;
                    return new Interval(visit.EnteredAt, end < visit.EnteredAt ? visit.EnteredAt : end);
                })
                .ToList();

            var mostRecentSeenAt = ordered.Max(visit => visit.LastSeenAt);
            var hasFreshOpenVisit = ordered.Any(visit =>
                visit.ClosedReason == null
                && snapshotAt - visit.LastSeenAt <= freshness);

            participants.Add(new SessionLogLobbyParticipant
            {
                AppUserId = group.Key,
                Role = person?.Role ?? ordered[0].Role,
                DisplayName = person?.DisplayName,
                FirstEnteredAt = ordered[0].EnteredAt,
                LastSeenAt = mostRecentSeenAt,
                LastLeftAt = ordered
                    .Where(visit => visit.LeftAt.HasValue)
                    .Select(visit => visit.LeftAt)
                    .Max(),
                TotalSeconds = TotalSeconds(Merge(windows)),
                VisitCount = ordered.Count,
                BeatCount = ordered.Sum(visit => visit.BeatCount),
                DisconnectCount = ordered.Count(visit =>
                    visit.ClosedReason == SessionLobbyVisitCloseReason.Disconnect),
                IsCurrentlyWaiting = isOngoing && hasFreshOpenVisit,
                WasAdmittedToRoom = admittedUsers.Contains(group.Key),
                Visits =
                [
                    .. ordered.Select(visit => new SessionLogLobbyVisit
                    {
                        EnteredAt = visit.EnteredAt,
                        LastSeenAt = visit.LastSeenAt,
                        LeftAt = visit.LeftAt,
                        BeatCount = visit.BeatCount,
                        ClosedReason = visit.ClosedReason,
                        ClosedReasonLabel = SessionLobbyVisitCloseReasonLabels.Label(visit.ClosedReason)
                    })
                ]
            });
        }

        var orderedParticipants = participants
            .OrderBy(participant => RoleOrder(participant.Role))
            .ThenBy(participant => participant.FirstEnteredAt)
            .ToList();
        var tutorRecorded = orderedParticipants.Any(participant =>
            participant.Role == SessionParticipantRole.Tutor);
        var studentHasOwnAccount = !string.IsNullOrEmpty(classSession.Booking?.Student?.Linkeduserid);
        var studentSideRecorded = orderedParticipants.Any(participant =>
            participant.Role == SessionParticipantRole.Student
            || (!studentHasOwnAccount && participant.Role == SessionParticipantRole.Parent));

        return new SessionLogLobbyEvidence
        {
            HasAnyRecord = true,
            TutorRecorded = tutorRecorded,
            StudentSideRecorded = studentSideRecorded,
            Participants = orderedParticipants
        };
    }

    // ── Networks and devices ──────────────────────────────────────────────────

    private static List<SessionLogDeviceUse> BuildDeviceUses(
        List<SessionParticipantDevice> devices,
        List<KnownParticipant> known,
        HashSet<string> flags)
    {
        if (devices.Count == 0)
        {
            flags.Add(SessionLogFlag.NoDeviceRecord);
            return [];
        }

        var byUser = known.ToDictionary(k => k.AppUserId, k => k, StringComparer.Ordinal);

        foreach (var group in devices.GroupBy(d => d.AppUserId, StringComparer.Ordinal))
        {
            // Only addresses and devices we actually captured can support a conclusion; rows whose
            // capture failed are stored as empty and must not inflate the distinct count.
            var networks = group
                .Select(d => d.IpAddress)
                .Where(ip => !string.IsNullOrEmpty(ip))
                .Distinct(StringComparer.Ordinal)
                .Count();
            var deviceIds = group
                .Select(d => d.DeviceId)
                .Where(id => !string.IsNullOrEmpty(id))
                .Distinct(StringComparer.Ordinal)
                .Count();

            if (networks > 1) flags.Add(SessionLogFlag.MultipleNetworks);
            if (deviceIds > 1) flags.Add(SessionLogFlag.MultipleDevices);
        }

        return
        [
            .. devices
                .Select(d =>
                {
                    byUser.TryGetValue(d.AppUserId, out var person);
                    return new SessionLogDeviceUse
                    {
                        AppUserId = d.AppUserId,
                        Role = person?.Role ?? d.Role,
                        DisplayName = person?.DisplayName,
                        IpAddress = d.IpAddress,
                        DeviceLabel = d.DeviceLabel,
                        UserAgent = d.UserAgent,
                        FirstSeenAt = d.FirstSeenAt,
                        LastSeenAt = d.LastSeenAt,
                        AdmissionCount = d.AdmissionCount
                    };
                })
                .OrderBy(d => RoleOrder(d.Role))
                .ThenBy(d => d.FirstSeenAt)
        ];
    }

    // ── Known participants ────────────────────────────────────────────────────

    /// <summary>
    /// The people we expect in the room, from the admission registry when available and from the
    /// booking itself otherwise. Sessions that ran before the registry existed still resolve, just
    /// without the timing signal — hence the flag.
    /// </summary>
    private static List<KnownParticipant> BuildKnownParticipants(
        ClassSession classSession,
        List<SessionParticipant> registry,
        HashSet<string> flags)
    {
        var tutorUserId = classSession.Tutorid;
        var studentUserId = classSession.Booking?.Student?.Linkeduserid;
        var parentUserId = classSession.Booking?.Parentid;

        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!string.IsNullOrEmpty(tutorUserId))
            names[tutorUserId] = classSession.Tutor?.Tutor?.Fullname ?? "Gia sư";
        if (!string.IsNullOrEmpty(studentUserId))
            names[studentUserId] = classSession.Booking?.Student?.Fullname ?? "Học viên";
        if (!string.IsNullOrEmpty(parentUserId))
            names[parentUserId] = classSession.Booking?.Parent?.Fullname ?? "Phụ huynh";

        string RoleOf(string userId)
        {
            if (userId == tutorUserId) return SessionParticipantRole.Tutor;
            if (userId == studentUserId) return SessionParticipantRole.Student;
            if (userId == parentUserId) return SessionParticipantRole.Parent;
            return SessionParticipantRole.Unknown;
        }

        if (registry.Count == 0)
            flags.Add(SessionLogFlag.NoParticipantRegistry);

        var admittedAt = registry.ToDictionary(p => p.AppUserId, p => p.FirstAdmittedAt, StringComparer.Ordinal);

        // Everyone the booking expects is listed whether or not they ever asked for a token — a
        // row with no presence is exactly the evidence that someone did not turn up. Admission
        // times are layered on top for those who did request one.
        var result = names
            .Select(kv => new KnownParticipant(
                kv.Key,
                RoleOf(kv.Key),
                kv.Value,
                admittedAt.TryGetValue(kv.Key, out var at) ? at : null))
            .ToList();

        foreach (var row in registry.Where(p => !names.ContainsKey(p.AppUserId)))
        {
            result.Add(new KnownParticipant(
                row.AppUserId,
                row.Role == SessionParticipantRole.Unknown ? RoleOf(row.AppUserId) : row.Role,
                null,
                row.FirstAdmittedAt));
        }

        return result;
    }

    // ── Event parsing ─────────────────────────────────────────────────────────

    private List<ParsedEvent> ParseEvents(
        List<AgoraChannelEvent> events,
        HashSet<string> flags)
    {
        var parsed = new List<ParsedEvent>(events.Count);

        foreach (var row in events)
        {
            JsonElement payload;
            try
            {
                using var document = JsonDocument.Parse(row.Payload);
                payload = document.RootElement.Clone();
            }
            catch (JsonException ex)
            {
                if (IsParticipantEvent(row.EventType))
                    flags.Add(SessionLogFlag.InsufficientEvidence);

                logger.LogWarning(
                    ex,
                    "Skipping Agora event {EventId} with unreadable payload in class session {ClassSessionId}.",
                    row.EventId,
                    row.ClassSessionId);
                continue;
            }

            var parsedEvent = new ParsedEvent(
                row.EventId,
                row.EventType,
                row.EventAt,
                row.ReceivedAt,
                ReadUid(payload),
                ReadText(payload, "account"),
                ReadLong(payload, "clientSeq"),
                ReadInt(payload, "clientType"),
                ReadInt(payload, "platform"),
                ReadInt(payload, "reason"),
                ReadInt(payload, "duration"));

            if (IsParticipantEvent(parsedEvent.EventType) && parsedEvent.Uid == null)
                flags.Add(SessionLogFlag.InsufficientEvidence);

            parsed.Add(parsedEvent);
        }

        return parsed;
    }

    /// <summary>
    /// Reads <c>uid</c> as text. Agora documents it as a number for RTC channel events but
    /// declares it a string elsewhere, so both shapes are accepted and normalised.
    /// </summary>
    private static string? ReadUid(JsonElement payload)
    {
        if (!payload.TryGetProperty("uid", out var uid))
            return null;

        return uid.ValueKind switch
        {
            JsonValueKind.String => uid.GetString(),
            JsonValueKind.Number => uid.GetRawText(),
            _ => null
        };
    }

    private static int? ReadInt(JsonElement payload, string propertyName)
    {
        if (!payload.TryGetProperty(propertyName, out var property))
            return null;

        return property.ValueKind switch
        {
            JsonValueKind.Number when property.TryGetInt32(out var value) => value,
            JsonValueKind.String when int.TryParse(
                property.GetString(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var parsed) => parsed,
            _ => null
        };
    }

    private static long? ReadLong(JsonElement payload, string propertyName)
    {
        if (!payload.TryGetProperty(propertyName, out var property))
            return null;

        return property.ValueKind switch
        {
            JsonValueKind.Number when property.TryGetInt64(out var value) => value,
            JsonValueKind.String when long.TryParse(
                property.GetString(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var parsed) => parsed,
            _ => null
        };
    }

    private static string? ReadText(JsonElement payload, string propertyName)
    {
        if (!payload.TryGetProperty(propertyName, out var property))
            return null;

        var value = property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Number => property.GetRawText(),
            _ => null
        };

        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    // ── Identity resolution ───────────────────────────────────────────────────

    /// <summary>
    /// Binds each Agora uid to a known participant. Exact id matches win outright; whatever is
    /// left is paired by comparing the uid's first join against admission times.
    /// </summary>
    private static Dictionary<string, Binding> ResolveIdentities(
        List<ParsedEvent> events,
        List<KnownParticipant> known,
        HashSet<string> recorderUids,
        HashSet<string> flags)
    {
        var bindings = new Dictionary<string, Binding>(StringComparer.Ordinal);

        var trafficByUid = events
            .Where(e => e.Uid != null
                        && IsParticipantEvent(e.EventType)
                        && !recorderUids.Contains(e.Uid))
            .GroupBy(e => e.Uid!, StringComparer.Ordinal)
            .ToList();

        // A later reconnect must not hide an earlier missing join. For every uid, take the oldest
        // evidence from either an explicit join or a leave's reported duration.
        var firstSeenByUid = trafficByUid.ToDictionary(
            group => group.Key,
            group => group.Min(EvidenceStart),
            StringComparer.Ordinal);
        var uidsWithReportedAccount = trafficByUid
            .Where(group => group.Any(e => !string.IsNullOrWhiteSpace(e.Account)))
            .Select(group => group.Key)
            .ToHashSet(StringComparer.Ordinal);

        var bound = new HashSet<string>(StringComparer.Ordinal);
        var inconsistentUids = new HashSet<string>(StringComparer.Ordinal);

        // Pass 1 — account is the application user account supplied to Agora at join time.
        foreach (var group in trafficByUid)
        {
            var accounts = group
                .Select(e => e.Account)
                .Where(account => !string.IsNullOrWhiteSpace(account))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            // A numeric uid reused with different accounts cannot safely be attributed as one
            // person. Do not cherry-pick the one account we happen to recognize.
            if (accounts.Count > 1)
            {
                flags.Add(SessionLogFlag.InsufficientEvidence);
                inconsistentUids.Add(group.Key);
                continue;
            }

            if (accounts.Count != 1)
                continue;

            var match = known.FirstOrDefault(participant =>
                string.Equals(participant.AppUserId, accounts[0], StringComparison.Ordinal));
            if (match == null)
                continue;

            bindings[group.Key] = new Binding(match, SessionLogIdentityConfidence.Exact);
            bound.Add(match.AppUserId);
        }

        // Pass 2 — older integrations may echo the application account directly as uid.
        foreach (var uid in firstSeenByUid.Keys.Where(uid =>
                     !bindings.ContainsKey(uid)
                     && !inconsistentUids.Contains(uid)
                     && !uidsWithReportedAccount.Contains(uid)))
        {
            var match = known.FirstOrDefault(k =>
                string.Equals(k.AppUserId, uid, StringComparison.Ordinal));
            if (match == null)
                continue;

            bindings[uid] = new Binding(match, SessionLogIdentityConfidence.Exact);
            bound.Add(match.AppUserId);
        }

        // Pass 3 — correlate what remains against admission times.
        var unresolved = firstSeenByUid
            .Where(kv => !bindings.ContainsKey(kv.Key) && !inconsistentUids.Contains(kv.Key))
            .OrderBy(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => (Uid: kv.Key, FirstSeen: kv.Value))
            .ToList();

        var candidates = known
            .Where(k => !bound.Contains(k.AppUserId) && k.AdmittedAt.HasValue)
            .OrderBy(k => k.AdmittedAt!.Value)
            .ToList();

        foreach (var (uid, participant) in MatchInOrder(unresolved, candidates))
        {
            bindings[uid] = new Binding(participant, SessionLogIdentityConfidence.Correlated);
            flags.Add(SessionLogFlag.IdentityUncertain);
        }

        return bindings;
    }

    private HashSet<string> FindRecorderUids(IEnumerable<ParsedEvent> events)
    {
        return events
            .Where(e => e.Uid != null
                        && (e.ClientType == 10
                            || (recorderUid != null
                                && (string.Equals(e.Uid, recorderUid, StringComparison.Ordinal)
                                    || string.Equals(e.Account, recorderUid, StringComparison.Ordinal)))))
            .Select(e => e.Uid!)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static bool IsParticipantEvent(short eventType)
        => AgoraChannelEventType.IsJoin(eventType) || AgoraChannelEventType.IsLeave(eventType);

    private static DateTime EvidenceStart(ParsedEvent e)
    {
        if (!AgoraChannelEventType.IsLeave(e.EventType)
            || !e.DurationSeconds.HasValue
            || e.DurationSeconds.Value <= 0)
        {
            return e.EventAt;
        }

        try
        {
            return e.EventAt.AddSeconds(-e.DurationSeconds.Value);
        }
        catch (ArgumentOutOfRangeException)
        {
            return e.EventAt;
        }
    }

    /// <summary>
    /// Pairs Agora uids with admitted users, both in chronological order.
    ///
    /// Picking the nearest admission for each uid independently gets this wrong whenever two
    /// people are admitted seconds apart — a greedy first choice can steal the partner the second
    /// uid needed. Instead this walks both sequences together and minimises the total time
    /// difference, allowing either side to go unmatched (a participant who never joined, or a uid
    /// that is not a participant at all, such as the cloud recorder). Sequences are at most a
    /// handful of entries, so the quadratic table is free.
    /// </summary>
    private static List<(string Uid, KnownParticipant Participant)> MatchInOrder(
        List<(string Uid, DateTime FirstSeen)> uids,
        List<KnownParticipant> candidates)
    {
        var m = uids.Count;
        var n = candidates.Count;
        if (m == 0 || n == 0) return [];

        // An unidentified uid is a real gap in the evidence, so leaving one unmatched is expensive.
        // An admitted user who never reached the channel is ordinary (a parent often requests a
        // token and stays out), so skipping a candidate is free — charging for it would push the
        // match onto whichever person happened to be admitted a second closer.
        const double unmatchedUidPenalty = 1_000_000d;
        const double impossible = double.PositiveInfinity;

        double Cost(int i, int j)
        {
            var admittedAt = candidates[j].AdmittedAt!.Value;
            var firstSeen = uids[i].FirstSeen;

            // Admission always precedes the media join; a small skew allowance covers clock drift.
            if (admittedAt > firstSeen + CorrelationSkew) return impossible;
            if (firstSeen - admittedAt > CorrelationWindow) return impossible;

            return Math.Abs((firstSeen - admittedAt).TotalSeconds);
        }

        var dp = new double[m + 1, n + 1];
        for (var j = 0; j <= n; j++) dp[m, j] = 0d;
        for (var i = 0; i <= m; i++) dp[i, n] = (m - i) * unmatchedUidPenalty;

        for (var i = m - 1; i >= 0; i--)
        {
            for (var j = n - 1; j >= 0; j--)
            {
                var matchCost = Cost(i, j);
                var best = unmatchedUidPenalty + dp[i + 1, j]; // this uid stays unidentified
                var skipCandidate = dp[i, j + 1];              // this person never joined
                if (skipCandidate < best) best = skipCandidate;

                if (!double.IsPositiveInfinity(matchCost))
                {
                    var paired = matchCost + dp[i + 1, j + 1];
                    if (paired < best) best = paired;
                }

                dp[i, j] = best;
            }
        }

        var result = new List<(string, KnownParticipant)>();
        for (int i = 0, j = 0; i < m && j < n;)
        {
            var matchCost = Cost(i, j);
            if (!double.IsPositiveInfinity(matchCost)
                && Math.Abs(dp[i, j] - (matchCost + dp[i + 1, j + 1])) < 1e-9)
            {
                result.Add((uids[i].Uid, candidates[j]));
                i++;
                j++;
            }
            else if (Math.Abs(dp[i, j] - (unmatchedUidPenalty + dp[i + 1, j])) < 1e-9)
            {
                i++;
            }
            else
            {
                j++;
            }
        }

        return result;
    }

    // ── Intervals and participants ────────────────────────────────────────────

    /// <summary>
    /// Chooses one end for every provisional interval in the response. Ongoing lessons use the
    /// captured snapshot; final lessons prefer checkout and then the last room event.
    /// </summary>
    private static DateTime? DetermineSessionEnd(
        List<ParsedEvent> events,
        ClassSession classSession,
        DateTime snapshotAt,
        bool isOngoing)
    {
        if (isOngoing)
            return snapshotAt;

        if (classSession.Checkouttime.HasValue)
            return classSession.Checkouttime;

        var destroy = events.LastOrDefault(e => e.EventType == AgoraChannelEventType.ChannelDestroy);
        if (destroy != null)
            return destroy.EventAt;

        return events.Count > 0 ? events[^1].EventAt : null;
    }

    private static bool IsOngoing(ClassSession classSession)
        => !classSession.Checkouttime.HasValue
           && classSession.Status is ClassSessionStatus.Scheduled or ClassSessionStatus.InProgress;

    private static List<SessionLogParticipant> BuildParticipants(
        List<ParsedEvent> events,
        Dictionary<string, Binding> bindings,
        List<KnownParticipant> known,
        HashSet<string> recorderUids,
        DateTime? sessionEnd,
        bool isOngoing,
        HashSet<string> flags)
    {
        var byUid = events
            .Where(e => e.Uid != null
                        && IsParticipantEvent(e.EventType))
            .GroupBy(e => e.Uid!, StringComparer.Ordinal);
        var roomDestroyTimes = events
            .Where(e => e.EventType == AgoraChannelEventType.ChannelDestroy)
            .Select(e => e.EventAt)
            .Distinct()
            .OrderBy(at => at)
            .ToList();

        var result = new List<SessionLogParticipant>();
        var seenUsers = new HashSet<string>(StringComparer.Ordinal);

        foreach (var group in byUid)
        {
            var uid = group.Key;
            var isRecorder = recorderUids.Contains(uid);
            Binding? binding = null;
            if (!isRecorder)
                bindings.TryGetValue(uid, out binding);

            var intervals = new List<Interval>();
            var disconnects = new List<SessionLogDisconnect>();
            var joinCount = 0;
            int? platform = null;
            var isCurrentlyPresent = false;

            // A channel destroy is authoritative evidence that every uid left the room. Partition
            // the uid's events into room epochs so a later rejoin can never make one continuous
            // interval across a period in which the channel did not exist.
            var roomEpochs = group
                .GroupBy(e => RoomEpoch(e, roomDestroyTimes))
                .OrderBy(epoch => epoch.Key);

            foreach (var epoch in roomEpochs)
            {
                DateTime? openedAt = null;
                var epochStart = epoch.Key > 0 ? roomDestroyTimes[epoch.Key - 1] : (DateTime?)null;

                foreach (var e in OrderParticipantEvents(epoch))
                {
                    platform ??= e.Platform;

                    if (AgoraChannelEventType.IsJoin(e.EventType))
                    {
                        joinCount++;
                        if (openedAt == null)
                        {
                            openedAt = e.EventAt;
                        }
                        // A second join without a leave means a state transition is missing. Keep
                        // the earlier start as an upper bound, but never let that estimate support
                        // a no-show/refund conclusion.
                        else if (!isRecorder)
                        {
                            flags.Add(SessionLogFlag.UnclosedInterval);
                        }
                        continue;
                    }

                    var reconstructingMissingJoin = openedAt == null;
                    var start = openedAt
                        // A leave with no open interval still carries how long the user had been in
                        // the channel, which reconstructs the missing join exactly.
                        ?? (e.DurationSeconds.HasValue ? EvidenceStart(e) : null);
                    var crossesDestroyedRoom = reconstructingMissingJoin
                                               && epochStart.HasValue
                                               && start.HasValue
                                               && start.Value < epochStart.Value;

                    if (reconstructingMissingJoin
                        && (!start.HasValue || start.Value >= e.EventAt || crossesDestroyedRoom)
                        && !isRecorder)
                    {
                        flags.Add(SessionLogFlag.InsufficientEvidence);
                    }
                    else if (!reconstructingMissingJoin
                             && start.HasValue
                             && start.Value > e.EventAt
                             && !isRecorder)
                    {
                        flags.Add(SessionLogFlag.InsufficientEvidence);
                    }

                    if (start.HasValue
                        && start.Value < e.EventAt
                        && !crossesDestroyedRoom)
                    {
                        if (reconstructingMissingJoin) joinCount++;
                        intervals.Add(new Interval(start.Value, e.EventAt));
                    }
                    // A leave always closes the current state, including a zero-length or invalid
                    // transition; otherwise it would be stretched to checkout and over-counted.
                    openedAt = null;

                    disconnects.Add(new SessionLogDisconnect
                    {
                        At = e.EventAt,
                        Reason = e.Reason,
                        ReasonLabel = AgoraLeaveReason.Label(e.Reason),
                        Involuntary = AgoraLeaveReason.IsInvoluntary(e.Reason),
                        SessionDurationSeconds = e.DurationSeconds
                    });

                    if (!isRecorder)
                    {
                        if (AgoraLeaveReason.IsInvoluntary(e.Reason)) flags.Add(SessionLogFlag.NetworkDrop);
                        if (e.Reason == AgoraLeaveReason.TokenError) flags.Add(SessionLogFlag.TokenError);
                        if (e.Reason == AgoraLeaveReason.UnusualActivity) flags.Add(SessionLogFlag.UnusualActivity);
                    }
                }

                if (openedAt.HasValue)
                {
                    DateTime closeAt;
                    if (epoch.Key < roomDestroyTimes.Count)
                    {
                        if (!isRecorder)
                            flags.Add(SessionLogFlag.UnclosedInterval);
                        closeAt = roomDestroyTimes[epoch.Key];
                    }
                    else
                    {
                        isCurrentlyPresent = isOngoing;
                        if (!isOngoing && !isRecorder)
                            flags.Add(SessionLogFlag.UnclosedInterval);

                        closeAt = sessionEnd.HasValue && sessionEnd.Value > openedAt.Value
                            ? sessionEnd.Value
                            : openedAt.Value;
                    }

                    intervals.Add(new Interval(openedAt.Value, closeAt));
                }
            }

            var merged = Merge(intervals);
            if (binding?.Participant != null)
                seenUsers.Add(binding.Participant.AppUserId);

            result.Add(new SessionLogParticipant
            {
                AppUserId = isRecorder ? null : binding?.Participant.AppUserId,
                Role = isRecorder
                    ? SessionParticipantRole.Recorder
                    : binding?.Participant.Role ?? SessionParticipantRole.Unknown,
                DisplayName = isRecorder ? RecorderDisplayName : binding?.Participant.DisplayName,
                AgoraUid = uid,
                IdentityConfidence = isRecorder
                    ? SessionLogIdentityConfidence.Exact
                    : binding?.Confidence ?? SessionLogIdentityConfidence.Unmatched,
                FirstJoinAt = merged.Count > 0 ? merged[0].Start : null,
                LastLeaveAt = isCurrentlyPresent || merged.Count == 0 ? null : merged[^1].End,
                TotalSeconds = TotalSeconds(merged),
                JoinCount = joinCount,
                IsCurrentlyPresent = isCurrentlyPresent,
                DropCount = disconnects.Count(d => d.Involuntary),
                Platform = platform.HasValue ? AgoraPlatform.Label(platform) : null,
                Disconnects = disconnects,
                Intervals = merged
            });
        }

        // Expected people who produced no Agora traffic at all still belong on the report — an
        // empty row is the evidence that they never showed up.
        foreach (var person in known.Where(k => !seenUsers.Contains(k.AppUserId)))
        {
            result.Add(new SessionLogParticipant
            {
                AppUserId = person.AppUserId,
                Role = person.Role,
                DisplayName = person.DisplayName,
                AgoraUid = null,
                IdentityConfidence = SessionLogIdentityConfidence.Unmatched,
                TotalSeconds = 0,
                JoinCount = 0,
                IsCurrentlyPresent = false,
                DropCount = 0,
                Intervals = []
            });
        }

        return [.. result.OrderBy(p => RoleOrder(p.Role)).ThenBy(p => p.FirstJoinAt ?? DateTime.MaxValue)];
    }

    private static int RoleOrder(string role) => role switch
    {
        SessionParticipantRole.Tutor => 0,
        SessionParticipantRole.Student => 1,
        SessionParticipantRole.Parent => 2,
        SessionParticipantRole.Recorder => 3,
        _ => 4
    };

    private static IEnumerable<ParsedEvent> OrderParticipantEvents(IEnumerable<ParsedEvent> source)
    {
        var events = source.ToList();
        if (events.Count > 0 && events.All(e => e.ClientSequence.HasValue))
        {
            return events
                .OrderBy(e => e.ClientSequence!.Value)
                .ThenBy(e => e.EventAt)
                .ThenBy(e => e.EventId);
        }

        return events
            .OrderBy(e => e.EventAt)
            .ThenBy(e => e.EventId);
    }

    private static int RoomEpoch(ParsedEvent participantEvent, List<DateTime> roomDestroyTimes)
        => roomDestroyTimes.Count(destroyAt =>
            destroyAt < participantEvent.EventAt
            || (destroyAt == participantEvent.EventAt
                && AgoraChannelEventType.IsJoin(participantEvent.EventType)));

    // ── Summary ───────────────────────────────────────────────────────────────

    private SessionLogSummary BuildSummary(
        ClassSession classSession,
        List<ParsedEvent> events,
        List<SessionLogParticipant> participants,
        List<SessionLogHeartbeat> heartbeats,
        DateTime snapshotAt,
        bool isOngoing,
        HashSet<string> flags)
    {
        var tutorIntervals = Merge([.. participants
            .Where(p => p.Role == SessionParticipantRole.Tutor)
            .SelectMany(p => p.Intervals)]);

        // A student without their own account attends under the parent login, so the parent's
        // presence has to count as the student side or every such lesson reads as a no-show.
        var studentHasOwnAccount = !string.IsNullOrEmpty(classSession.Booking?.Student?.Linkeduserid);
        var studentSide = participants
            .Where(p => p.Role == SessionParticipantRole.Student)
            .ToList();
        if (!studentHasOwnAccount)
        {
            studentSide = [.. studentSide.Concat(participants.Where(p => p.Role == SessionParticipantRole.Parent))];
        }
        var studentIntervals = Merge([.. studentSide.SelectMany(p => p.Intervals)]);

        var overlap = TotalSeconds(Intersect(tutorIntervals, studentIntervals));
        var scheduledSeconds = (int)Math.Max(
            0,
            (classSession.Scheduledend - classSession.Scheduledstart).TotalSeconds);

        var ratio = scheduledSeconds > 0
            ? Math.Round(Math.Clamp(overlap / (double)scheduledSeconds, 0d, 1d), 4)
            : 0d;

        // The same reduction over our own beats, kept beside the Agora figures instead of merged
        // into them: two sources that agree strengthen a decision, and one that disagrees is itself
        // the thing staff need to see.
        var tutorBeatIntervals = HeartbeatIntervals(heartbeats, role => role == SessionParticipantRole.Tutor);
        var studentBeatIntervals = HeartbeatIntervals(
            heartbeats,
            role => role == SessionParticipantRole.Student
                    || (!studentHasOwnAccount && role == SessionParticipantRole.Parent));
        var beatOverlap = TotalSeconds(Intersect(tutorBeatIntervals, studentBeatIntervals));

        // Our heartbeat only checks a lesson in when both sides are present, so a check-in with no
        // shared Agora time means the sources disagree. Such a report is evidence, but not final
        // evidence from which a no-show or refund should be inferred.
        if (events.Count > 0 && classSession.Checkintime.HasValue && overlap == 0)
            flags.Add(SessionLogFlag.CheckInMismatch);

        // Beats without matching Agora presence almost always mean notifications are not reaching
        // us. Whatever the cause, the Agora side is incomplete, so it cannot carry a no-show.
        if ((tutorBeatIntervals.Count > 0 && tutorIntervals.Count == 0)
            || (studentBeatIntervals.Count > 0 && studentIntervals.Count == 0))
        {
            flags.Add(SessionLogFlag.PresenceWithoutAgora);
        }

        var hasUnmatchedHumanTraffic = participants.Any(p =>
            p.AgoraUid != null && p.Role == SessionParticipantRole.Unknown);
        var hasResolvedHumanEvent = participants.Any(p =>
            p.AgoraUid != null && IsHumanRole(p.Role));

        var isEvidenceConclusive =
            !isOngoing
            && !hasUnmatchedHumanTraffic
            && !flags.Contains(SessionLogFlag.IdentityUncertain)
            && !flags.Contains(SessionLogFlag.UnclosedInterval)
            && !flags.Contains(SessionLogFlag.CheckInMismatch)
            && !flags.Contains(SessionLogFlag.InsufficientEvidence)
            && !flags.Contains(SessionLogFlag.PresenceWithoutAgora)
            && hasResolvedHumanEvent;

        if (isEvidenceConclusive)
        {
            if (tutorIntervals.Count == 0) flags.Add(SessionLogFlag.TutorNeverJoined);
            if (studentIntervals.Count == 0) flags.Add(SessionLogFlag.StudentNeverJoined);
            if (overlap == 0) flags.Add(SessionLogFlag.ZeroOverlap);
        }

        var maxLag = events.Count > 0
            ? (int)events.Max(e => Math.Max(0, (e.ReceivedAt - e.EventAt).TotalSeconds))
            : 0;

        // Punctuality prefers Agora because its clock is not the tutor's own, and falls back to the
        // beats so a lesson with no notifications is still measurable — labelled, never silently.
        (List<Interval> punctualityIntervals, string? punctualitySource) = tutorIntervals.Count > 0
            ? (tutorIntervals, SessionLogPunctualitySource.Agora)
            : tutorBeatIntervals.Count > 0
                ? (tutorBeatIntervals, SessionLogPunctualitySource.Heartbeat)
                : (new List<Interval>(), null);

        return new SessionLogSummary
        {
            SnapshotAt = snapshotAt,
            IsOngoing = isOngoing,
            IsEvidenceConclusive = isEvidenceConclusive,
            ScheduledStart = classSession.Scheduledstart,
            ScheduledEnd = classSession.Scheduledend,
            ScheduledSeconds = scheduledSeconds,
            CheckInTime = classSession.Checkintime,
            CheckOutTime = classSession.Checkouttime,
            FirstEventAt = events.Count > 0 ? events[0].EventAt : null,
            LastEventAt = events.Count > 0 ? events[^1].EventAt : null,
            TutorSeconds = TotalSeconds(tutorIntervals),
            StudentSeconds = TotalSeconds(studentIntervals),
            OverlapSeconds = overlap,
            OverlapRatio = ratio,
            SuggestedRefundPercentage = SuggestRefund(ratio, isEvidenceConclusive),
            EventCount = events.Count,
            MaxIngestLagSeconds = maxLag,
            TutorHeartbeatSeconds = TotalSeconds(tutorBeatIntervals),
            StudentHeartbeatSeconds = TotalSeconds(studentBeatIntervals),
            HeartbeatOverlapSeconds = beatOverlap,
            HeartbeatOverlapRatio = scheduledSeconds > 0
                ? Math.Round(Math.Clamp(beatOverlap / (double)scheduledSeconds, 0d, 1d), 4)
                : 0d,
            HeartbeatCount = heartbeats.Sum(h => h.BeatCount),
            TutorLateSeconds = LateSeconds(punctualityIntervals, classSession.Scheduledstart),
            TutorEarlyLeaveSeconds = isOngoing
                ? null
                : EarlyLeaveSeconds(punctualityIntervals, classSession.Scheduledend),
            PunctualitySource = punctualitySource
        };
    }

    /// <summary>Merged beat windows of every participant whose role passes <paramref name="matches"/>.</summary>
    private static List<Interval> HeartbeatIntervals(
        List<SessionLogHeartbeat> heartbeats,
        Func<string, bool> matches)
    {
        return Merge([.. heartbeats
            .Where(h => matches(h.Role))
            .SelectMany(h => h.Runs.Select(r => new Interval(r.StartedAt, r.LastBeatAt)))]);
    }

    /// <summary>
    /// Seconds past the grace period that the first arrival came after the scheduled start.
    /// Null when there was no arrival at all — absence is reported as a no-show flag, not as
    /// infinite lateness.
    /// </summary>
    private int? LateSeconds(List<Interval> presence, DateTime scheduledStart)
    {
        if (presence.Count == 0)
            return null;

        var lateBy = (presence[0].Start - scheduledStart).TotalSeconds - evidence.PunctualityGraceSeconds;
        return (int)Math.Max(0, lateBy);
    }

    /// <summary>
    /// Seconds past the grace period that the last departure came before the scheduled end. Null
    /// when there was no presence to leave from.
    /// </summary>
    private int? EarlyLeaveSeconds(List<Interval> presence, DateTime scheduledEnd)
    {
        if (presence.Count == 0)
            return null;

        var leftEarlyBy = (scheduledEnd - presence[^1].End).TotalSeconds - evidence.PunctualityGraceSeconds;
        return (int)Math.Max(0, leftEarlyBy);
    }

    /// <summary>
    /// Advisory refund band. Deliberately coarse — this is a starting point for a staff decision,
    /// not an automated payout. Returns null until the evidence is conclusive.
    /// </summary>
    private static int? SuggestRefund(double ratio, bool isEvidenceConclusive)
    {
        if (!isEvidenceConclusive) return null;
        if (ratio < 0.25) return 100;
        if (ratio < 0.75) return 50;
        return 0;
    }

    private static bool IsHumanRole(string role)
        => role is SessionParticipantRole.Tutor
            or SessionParticipantRole.Student
            or SessionParticipantRole.Parent;

    // ── Timeline ──────────────────────────────────────────────────────────────

    private static List<SessionLogEvent> BuildTimeline(
        List<ParsedEvent> events,
        Dictionary<string, Binding> bindings,
        HashSet<string> recorderUids)
    {
        return [.. events.Select(e =>
        {
            var isRecorder = e.Uid != null && recorderUids.Contains(e.Uid);
            Binding? binding = null;
            if (!isRecorder && e.Uid != null)
                bindings.TryGetValue(e.Uid, out binding);

            var isLeave = AgoraChannelEventType.IsLeave(e.EventType);

            return new SessionLogEvent
            {
                EventAt = e.EventAt,
                ReceivedAt = e.ReceivedAt,
                EventType = e.EventType,
                EventLabel = EventLabel(e.EventType),
                AgoraUid = e.Uid,
                AgoraAccount = e.Account,
                AppUserId = isRecorder ? null : binding?.Participant.AppUserId,
                Role = isRecorder ? SessionParticipantRole.Recorder : binding?.Participant.Role,
                DisplayName = isRecorder ? RecorderDisplayName : binding?.Participant.DisplayName,
                ClientSequence = e.ClientSequence,
                ClientType = e.ClientType,
                Platform = e.Platform,
                PlatformLabel = e.Platform.HasValue ? AgoraPlatform.Label(e.Platform) : null,
                Reason = isLeave ? e.Reason : null,
                ReasonLabel = isLeave ? AgoraLeaveReason.Label(e.Reason) : null,
                DurationSeconds = e.DurationSeconds
            };
        })];
    }

    private static string EventLabel(short eventType)
    {
        if (AgoraChannelEventType.IsJoin(eventType))
            return "Vào phòng";
        if (AgoraChannelEventType.IsLeave(eventType))
            return "Rời phòng";

        return eventType switch
        {
            AgoraChannelEventType.ChannelCreate => "Phòng học được mở",
            AgoraChannelEventType.ChannelDestroy => "Phòng học đóng",
            _ => $"Sự kiện {eventType}"
        };
    }

    // ── Interval maths ────────────────────────────────────────────────────────

    /// <summary>Sorts and unions overlapping intervals so rejoins are never counted twice.</summary>
    public static List<Interval> Merge(List<Interval> intervals)
    {
        if (intervals.Count == 0) return [];

        var ordered = intervals
            .Where(i => i.End > i.Start)
            .OrderBy(i => i.Start)
            .ToList();

        if (ordered.Count == 0) return [];

        var merged = new List<Interval> { ordered[0] };
        foreach (var current in ordered.Skip(1))
        {
            var last = merged[^1];
            if (current.Start <= last.End)
                merged[^1] = new Interval(last.Start, current.End > last.End ? current.End : last.End);
            else
                merged.Add(current);
        }

        return merged;
    }

    /// <summary>Intersection of two already-merged interval lists.</summary>
    public static List<Interval> Intersect(List<Interval> left, List<Interval> right)
    {
        var result = new List<Interval>();
        int i = 0, j = 0;

        while (i < left.Count && j < right.Count)
        {
            var start = left[i].Start > right[j].Start ? left[i].Start : right[j].Start;
            var end = left[i].End < right[j].End ? left[i].End : right[j].End;

            if (end > start)
                result.Add(new Interval(start, end));

            if (left[i].End < right[j].End) i++;
            else j++;
        }

        return result;
    }

    public static int TotalSeconds(List<Interval> intervals)
        => (int)intervals.Sum(i => (i.End - i.Start).TotalSeconds);

    // ── Internal shapes ───────────────────────────────────────────────────────

    private sealed record ParsedEvent(
        long EventId,
        short EventType,
        DateTime EventAt,
        DateTime ReceivedAt,
        string? Uid,
        string? Account,
        long? ClientSequence,
        int? ClientType,
        int? Platform,
        int? Reason,
        int? DurationSeconds);

    private sealed record KnownParticipant(
        string AppUserId,
        string Role,
        string? DisplayName,
        DateTime? AdmittedAt);

    private sealed record Binding(KnownParticipant Participant, string Confidence);
}
