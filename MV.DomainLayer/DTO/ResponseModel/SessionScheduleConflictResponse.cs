namespace MV.DomainLayer.DTO.ResponseModel;

/// <summary>
/// A class session that overlaps the proposed off-schedule interval for the
/// same tutor, the same student, or both.
/// </summary>
public record SessionScheduleConflictResponse(
    int ClassSessionId,
    DateTime ScheduledStart,
    DateTime ScheduledEnd,
    string ConflictingParty,
    string Message
);