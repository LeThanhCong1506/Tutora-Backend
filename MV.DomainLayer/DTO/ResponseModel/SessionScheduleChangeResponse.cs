namespace MV.DomainLayer.DTO.ResponseModel;

public class SessionScheduleChangeResponse
{
    public int ClassSessionId { get; set; }
    public bool RequiresConfirmation { get; set; }
    public bool CanCurrentUserConfirm { get; set; }
    public bool CurrentUserConfirmed { get; set; }
    public bool AdmissionAllowed { get; set; }
    public string? Status { get; set; }
    public string? TutorUserId { get; set; }
    public string? LearnerApproverUserId { get; set; }
    public string? RequiredLearnerRole { get; set; }
    public string? RequiredLearnerName { get; set; }
    public string? TutorName { get; set; }
    public string? StudentName { get; set; }
    public DateTime OriginalScheduledStart { get; set; }
    public DateTime OriginalScheduledEnd { get; set; }
    public int DurationMinutes { get; set; }
    public DateTime? RequestedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime? TutorConfirmedAt { get; set; }
    public DateTime? LearnerConfirmedAt { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public DateTime? AppliedAt { get; set; }
    public DateTime? AdjustedScheduledStart { get; set; }
    public DateTime? AdjustedScheduledEnd { get; set; }
    public SessionScheduleConflictResponse? ScheduleConflict { get; set; }
}

public class DisputeScheduleChangeAuditResponse
{
    public int ScheduleChangeId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime OriginalScheduledStart { get; set; }
    public DateTime OriginalScheduledEnd { get; set; }
    public DateTime? AdjustedScheduledStart { get; set; }
    public DateTime? AdjustedScheduledEnd { get; set; }
    public string LearnerApproverRole { get; set; } = string.Empty;
    public string? TutorConfirmedByName { get; set; }
    public DateTime? TutorConfirmedAt { get; set; }
    public string? LearnerConfirmedByName { get; set; }
    public DateTime? LearnerConfirmedAt { get; set; }
    public DateTime? RequestedAt { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public DateTime? AppliedAt { get; set; }
}
