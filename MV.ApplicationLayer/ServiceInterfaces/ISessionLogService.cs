using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;

namespace MV.ApplicationLayer.ServiceInterfaces;

public interface ISessionLogService
{
    /// <summary>
    /// Rebuilds the attendance timeline of a lesson from captured Agora channel events, the
    /// heartbeat chain our own classroom client sent, and the networks each participant came from.
    /// Returns null when the class session does not exist.
    /// </summary>
    Task<SessionLogResponse?> GetSessionLogAsync(int classSessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Punctuality and reliability of one tutor over a date range, aggregated from the same
    /// evidence <see cref="GetSessionLogAsync"/> reports per lesson.
    /// </summary>
    Task<TutorReliabilityResponse> GetTutorReliabilityAsync(
        string tutorUserId,
        DateTime fromDate,
        DateTime toDate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records that a user was admitted into a session room, so the log can later bind Agora's
    /// channel uid to a real participant, and keeps the network/device they arrived from.
    /// Best-effort: never throws, never blocks admission.
    /// </summary>
    Task RecordAdmissionAsync(
        int classSessionId,
        string appUserId,
        string role,
        SessionAdmissionContext? context = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Extends (or opens) the caller's heartbeat run for this lesson. Best-effort: a failure here
    /// must never break the beat that keeps the classroom alive.
    /// </summary>
    Task RecordHeartbeatAsync(
        int classSessionId,
        string appUserId,
        string role,
        LiveSessionActivityReport? activity,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Closes the caller's open heartbeat run as an intentional exit, so a deliberate departure is
    /// distinguishable from beats that simply stopped. Best-effort.
    /// </summary>
    Task CloseHeartbeatAsync(
        int classSessionId,
        string appUserId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Where an admission came from. Every field is optional — evidence we could not capture is stored
/// as empty rather than guessed.
/// </summary>
public sealed record SessionAdmissionContext(
    string? IpAddress,
    string? DeviceId,
    string? DeviceLabel,
    string? UserAgent);
