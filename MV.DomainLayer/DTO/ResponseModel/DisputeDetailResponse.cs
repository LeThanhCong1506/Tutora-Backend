namespace MV.DomainLayer.DTO.ResponseModel;

/// <summary>
/// Dispute detail for admin view
/// </summary>
public class DisputeDetailResponse
{
    public int DisputeId { get; set; }
    public int? BookingId { get; set; }
    public int? ClassSessionId { get; set; }

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

    // Tutor rebuttal
    public string? TutorResponse { get; set; }
    public DateTime? TutorRespondedAt { get; set; }
    public List<DisputeEvidenceItemResponse>? TutorEvidence { get; set; }

    // Created by info
    public DisputeUserResponse? CreatedBy { get; set; }
    public DisputeUserResponse? ResolvedBy { get; set; }

    // Related classSession info
    public DisputeClassSessionResponse? ClassSession { get; set; }

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

public class DisputeClassSessionResponse
{
    public int ClassSessionId { get; set; }
    public DateTime ScheduledStart { get; set; }
    public DateTime ScheduledEnd { get; set; }
    public string? Status { get; set; }
    public decimal? ClassSessionPrice { get; set; }
    public string? ClassSessionContent { get; set; }
    public string? Homework { get; set; }
    public bool? IsTutorPresent { get; set; }
    public bool? IsStudentPresent { get; set; }

    /// <summary>Trạng thái bản ghi video: available | processing | recording | none.</summary>
    public string? RecordingStatus { get; set; }

    /// <summary>Link xem video buổi học (Google Drive) — chỉ có khi RecordingStatus = "available".</summary>
    public string? RecordingUrl { get; set; }
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

/// <summary>Bằng chứng gia sư nộp thêm sau khi tranh chấp đã được tạo (bảng dispute_evidences).</summary>
public class DisputeEvidenceItemResponse
{
    public int DisputeEvidenceId { get; set; }
    public string? FileUrl { get; set; }
    public string? FileType { get; set; }
    public string? Description { get; set; }
    public DateTime? CreatedAt { get; set; }
}

/// <summary>
/// Thông tin bản ghi video của buổi học gắn với tranh chấp — cho Admin/Staff xem khi xử lý.
/// </summary>
public class DisputeRecordingResponse
{
    public int DisputeId { get; set; }
    public int? ClassSessionId { get; set; }

    /// <summary>available (có link xem) | processing (đang đẩy lên Drive) | recording (đang ghi) | none.</summary>
    public string Status { get; set; } = "none";

    /// <summary>Link xem video (Google Drive) — chỉ có khi Status = "available".</summary>
    public string? RecordingUrl { get; set; }

    /// <summary>True nếu đã có link xem được.</summary>
    public bool Available { get; set; }
}
