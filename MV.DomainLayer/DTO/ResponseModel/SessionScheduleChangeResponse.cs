namespace MV.DomainLayer.DTO.ResponseModel;

public class SessionScheduleChangeResponse
{
    public int ClassSessionId { get; set; }
    public bool RequiresConfirmation { get; set; }
    public bool CanCurrentUserConfirm { get; set; }
    public bool CurrentUserConfirmed { get; set; }
    public bool AdmissionAllowed { get; set; }
    /// <summary>
    /// True nếu buổi học đang có một đề xuất đổi lịch (tính năng chủ động chọn giờ mới,
    /// <c>ClassSessionRescheduleProposal</c>) đang chờ phản hồi — cổng xác nhận vào học ngoài giờ
    /// bị khoá hoàn toàn cho tới khi đề xuất đó được xử lý xong, để 2 cơ chế đổi giờ không đụng độ.
    /// Khi true, FE không được cho vào phòng dù <see cref="RequiresConfirmation"/> là false.
    /// </summary>
    public bool RescheduleProposalPending { get; set; }
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
    /// <summary>UserId của bên đã bấm "Từ chối" khi <see cref="Status"/> là "rejected" — so với
    /// <see cref="TutorUserId"/>/<see cref="LearnerApproverUserId"/> để biết hiển thị "Đã từ chối"
    /// ở dòng nào trong danh sách xác nhận.</summary>
    public string? RejectedByUserId { get; set; }
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
