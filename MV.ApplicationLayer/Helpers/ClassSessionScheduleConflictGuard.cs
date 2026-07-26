using Microsoft.EntityFrameworkCore;
using MV.ApplicationLayer.Interfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Helpers;

namespace MV.ApplicationLayer.Helpers;

/// <summary>
/// Shared overlap guard for off-schedule admission. Intervals use [start, end),
/// therefore two sessions that only touch at the boundary are allowed.
/// </summary>
public static class ClassSessionScheduleConflictGuard
{
    public static async Task<SessionScheduleConflictResponse?> FindAsync(
        IAppDbContext db,
        int classSessionId,
        string? tutorId,
        string? studentId,
        DateTime candidateStart,
        DateTime candidateEnd,
        CancellationToken cancellationToken = default,
        DateTime? currentApprovedAt = null,
        int? currentScheduleChangeId = null)
    {
        if (candidateEnd <= candidateStart)
            throw new ArgumentException("Thời gian kết thúc phải sau thời gian bắt đầu.");

        var sessionConflict = await db.ClassSessions
            .AsNoTracking()
            .Where(x => x.Classsessionid != classSessionId
                && x.Checkouttime == null
                && (x.Status == ClassSessionStatus.Reserved
                    || x.Status == ClassSessionStatus.Scheduled
                    || x.Status == ClassSessionStatus.InProgress)
                && ((!string.IsNullOrEmpty(tutorId) && x.Tutorid == tutorId)
                    || (!string.IsNullOrEmpty(studentId) && x.Studentid == studentId))
                && x.Scheduledstart < candidateEnd
                && x.Scheduledend > candidateStart)
            .OrderBy(x => x.Scheduledstart)
            .Select(x => new ConflictCandidate(
                x.Classsessionid,
                x.Scheduledstart,
                x.Scheduledend,
                x.Tutorid,
                x.Studentid))
            .FirstOrDefaultAsync(cancellationToken);

        if (sessionConflict != null)
            return Map(sessionConflict, tutorId, studentId);

        // A fully approved change reserves a short-lived candidate interval from
        // approval time. The final adjusted interval is still written only when
        // both participants actually enter, preserving the current audit behavior.
        var now = TimeZoneHelper.UtcNow;
        var reservations = await db.ClassSessionScheduleChanges
            .AsNoTracking()
            .Where(x => x.Classsessionid != classSessionId
                && x.Status == ScheduleChangeStatus.Approved
                && x.Approvedat.HasValue
                && x.Expiresat > now
                && ((!string.IsNullOrEmpty(tutorId) && x.ClassSession.Tutorid == tutorId)
                    || (!string.IsNullOrEmpty(studentId) && x.ClassSession.Studentid == studentId)))
            .Select(x => new
            {
                x.Schedulechangeid,
                x.Classsessionid,
                x.Approvedat,
                x.Originalscheduledstart,
                x.Originalscheduledend,
                x.ClassSession.Tutorid,
                x.ClassSession.Studentid
            })
            .ToListAsync(cancellationToken);

        var reservationConflict = reservations
            .Where(x => !currentApprovedAt.HasValue
                || x.Approvedat!.Value < currentApprovedAt.Value
                || (x.Approvedat.Value == currentApprovedAt.Value
                    && (!currentScheduleChangeId.HasValue || x.Schedulechangeid < currentScheduleChangeId.Value)))
            .Select(x =>
            {
                var start = x.Approvedat!.Value;
                return new ConflictCandidate(
                    x.Classsessionid,
                    start,
                    start.Add(x.Originalscheduledend - x.Originalscheduledstart),
                    x.Tutorid,
                    x.Studentid);
            })
            .Where(x => x.Start < candidateEnd && x.End > candidateStart)
            .OrderBy(x => x.Start)
            .FirstOrDefault();

        return reservationConflict == null ? null : Map(reservationConflict, tutorId, studentId);
    }

    private static SessionScheduleConflictResponse Map(
        ConflictCandidate conflict,
        string? tutorId,
        string? studentId)
    {
        var sameTutor = !string.IsNullOrEmpty(tutorId) && conflict.TutorId == tutorId;
        var sameStudent = !string.IsNullOrEmpty(studentId) && conflict.StudentId == studentId;
        var party = sameTutor && sameStudent
            ? "tutor_and_student"
            : sameTutor ? "tutor" : "student";
        var partyLabel = party switch
        {
            "tutor" => "Gia sư",
            "student" => "Học sinh",
            _ => "Gia sư và học sinh"
        };

        var vietnamTimeZone = TimeZoneHelper.GetTimeZoneInfo("Asia/Ho_Chi_Minh");
        var start = TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(conflict.Start, DateTimeKind.Utc),
            vietnamTimeZone);
        var end = TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(conflict.End, DateTimeKind.Utc),
            vietnamTimeZone);
        var message =
            $"{partyLabel} đang có buổi học #{conflict.ClassSessionId} " +
            $"từ {start:HH:mm} đến {end:HH:mm} ngày {start:dd/MM/yyyy}. " +
            "Vui lòng bắt đầu buổi học này sau khi lịch trên kết thúc.";

        return new SessionScheduleConflictResponse(
            conflict.ClassSessionId,
            conflict.Start,
            conflict.End,
            party,
            message);
    }

    private sealed record ConflictCandidate(
        int ClassSessionId,
        DateTime Start,
        DateTime End,
        string? TutorId,
        string? StudentId);
}