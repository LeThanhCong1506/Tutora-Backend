namespace MV.DomainLayer.DTO.ResponseModel;

/// <summary>
/// Dispute detail for admin view
/// </summary>
public class DisputeDetailResponse
{
    public int DisputeId { get; set; }
    public int? BookingId { get; set; }
    public int? LessonId { get; set; }

    public string? DisputeType { get; set; }
    public string? Reason { get; set; }
    public string? Status { get; set; }
    public List<string>? Evidence { get; set; }

    public DateTime? CreatedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }

    // Resolution info
    public string? ResolutionNote { get; set; }
    public decimal? RefundAmount { get; set; }
    public int? RefundPercentage { get; set; }

    // Created by info
    public DisputeUserResponse? CreatedBy { get; set; }
    public DisputeUserResponse? ResolvedBy { get; set; }

    // Related lesson info
    public DisputeLessonResponse? Lesson { get; set; }

    // Tutor info
    public DisputeTutorResponse? Tutor { get; set; }

    // Time since creation
    public string? TimeSinceCreation
    {
        get
        {
            if (!CreatedAt.HasValue) return null;
            var elapsed = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow - CreatedAt.Value;
            if (elapsed.TotalDays >= 1)
                return $"{(int)elapsed.TotalDays} ngày trước";
            if (elapsed.TotalHours >= 1)
                return $"{(int)elapsed.TotalHours} giờ trước";
            return $"{(int)elapsed.TotalMinutes} phút trước";
        }
    }
}

public class DisputeUserResponse
{
    public string? UserId { get; set; }
    public string? FullName { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? AvatarUrl { get; set; }
}

public class DisputeLessonResponse
{
    public int LessonId { get; set; }
    public DateTime ScheduledStart { get; set; }
    public DateTime ScheduledEnd { get; set; }
    public string? Status { get; set; }
    public decimal? LessonPrice { get; set; }
    public string? LessonContent { get; set; }
    public string? Homework { get; set; }
    public bool? IsTutorPresent { get; set; }
    public bool? IsStudentPresent { get; set; }
}

public class DisputeTutorResponse
{
    public string? TutorId { get; set; }
    public string? FullName { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public int WarningCount { get; set; }
    public decimal? AverageRating { get; set; }
}
