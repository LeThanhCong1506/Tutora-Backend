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

    /// <summary>
    /// True khi hai bên đang vào lớp TRƯỚC giờ hẹn. Từ khi bỏ bước xác nhận học sớm, người dùng
    /// không còn dấu hiệu nào cho biết mình đang vào sớm — chỉ thấy phòng mở bình thường, dễ tưởng
    /// đã tới giờ và trách nhầm bên kia đến muộn ở buổi sau.
    ///
    /// Tính ở server: đồng hồ máy người dùng có thể lệch, mà đây là thứ so với giờ hẹn.
    /// </summary>
    public bool IsEarlyEntry { get; set; }

    /// <summary>Số phút còn lại tới giờ hẹn khi <see cref="IsEarlyEntry"/> đúng; 0 nếu không.</summary>
    public int MinutesEarly { get; set; }

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
