using MV.DomainLayer.Constants;

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

    /// <summary>AI-classified priority (low/medium/high) — null until the background classification job runs.</summary>
    public string? Priority { get; set; }
    /// <summary>Short AI justification for <see cref="Priority"/>.</summary>
    public string? PriorityReason { get; set; }
    /// <summary>Display priority with icon — "Chưa phân loại" while unclassified.</summary>
    public string PriorityDisplay => Priority switch
    {
        DisputePriority.High => "🔴 Cao",
        DisputePriority.Medium => "🟡 Trung bình",
        DisputePriority.Low => "🟢 Thấp",
        _ => "Chưa phân loại"
    };

    public DateTime? CreatedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }

    // Resolution info
    public string? ResolutionNote { get; set; }
    public decimal? RefundAmount { get; set; }
    public int? RefundPercentage { get; set; }

    // Tutor rebuttal
    public string? TutorResponse { get; set; }
    public DateTime? TutorRespondedAt { get; set; }
    public List<DisputeEvidenceItemResponse>? AdditionalEvidence { get; set; }

    // No-show verification (admin gate before the payer side may choose a remedy)
    public DateTime? NoShowConfirmedAt { get; set; }
    public string? NoShowConfirmedBy { get; set; }

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

    /// <summary>Earliest time admin can call Investigate without forceEarly (Createdat + 48h).</summary>
    public DateTime? TutorResponseDeadline => CreatedAt?.AddHours(48);
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

    public List<DisputeScheduleChangeAuditResponse> ScheduleChanges { get; set; } = new();

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
    public string? AvatarUrl { get; set; }
    public int WarningCount { get; set; }
    public decimal? AverageRating { get; set; }
}

/// <summary>Bằng chứng nộp thêm sau khi tranh chấp đã được tạo — parent/student hoặc gia sư (bảng dispute_evidences).</summary>
public class DisputeEvidenceItemResponse
{
    public int DisputeEvidenceId { get; set; }
    public string? FileUrl { get; set; }
    public string? FileType { get; set; }
    public string? Description { get; set; }
    public DateTime? CreatedAt { get; set; }
    /// <summary>Source party used by the admin UI to keep learner and tutor evidence separate.</summary>
    public string Source { get; set; } = "unknown";
    public string? UploadedByName { get; set; }
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
