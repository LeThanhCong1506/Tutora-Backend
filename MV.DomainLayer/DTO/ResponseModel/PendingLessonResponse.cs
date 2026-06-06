using MV.DomainLayer.Helpers;

namespace MV.DomainLayer.DTO.ResponseModel;

/// <summary>
/// Lesson awaiting parent confirmation after tutor submits a report.
/// Callers: <c>GetPendingLessonsAsync</c> (parent + tutor via ParentLessonService),
/// <c>GetPendingSettlementsAsync</c> (SettlementService for admin payout queue).
/// Includes computed helpers: <see cref="TimeRemainingDisplay"/> and <see cref="IsUrgent"/>.
/// All DateTime fields are Vietnam time (UTC+7).
/// </summary>
public class PendingLessonResponse
{
    // So sánh dùng giờ VN vì ConfirmDeadline đã được convert sang giờ VN (now using VietnamTimeHelper)

    public int LessonId { get; set; }
    public int? BookingId { get; set; }

    public DateTime ScheduledStart { get; set; }
    public DateTime ScheduledEnd { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public DateTime? ConfirmDeadline { get; set; }

    public string? TutorName { get; set; }
    public string? TutorAvatarUrl { get; set; }
    public string? StudentName { get; set; }
    public string? SubjectName { get; set; }

    public decimal? LessonPrice { get; set; }

    /// <summary>
    /// Report content from tutor
    /// </summary>
    public string? LessonContent { get; set; }
    public string? Homework { get; set; }
    public string? TutorNotes { get; set; }

    /// <summary>
    /// Time remaining to confirm (hours and minutes display)
    /// </summary>
    public string? TimeRemainingDisplay
    {
        get
        {
            if (!ConfirmDeadline.HasValue || ConfirmDeadline <= VietnamTimeHelper.Now)
                return "Hết hạn";

            var remaining = ConfirmDeadline.Value - VietnamTimeHelper.Now;
            if (remaining.TotalHours >= 1)
                return $"{(int)remaining.TotalHours}h {remaining.Minutes}m";
            return $"{remaining.Minutes}m";
        }
    }

    /// <summary>
    /// Is deadline approaching (less than 4 hours)
    /// </summary>
    public bool IsUrgent => ConfirmDeadline.HasValue &&
        (ConfirmDeadline.Value - VietnamTimeHelper.Now).TotalHours < 4;
}
