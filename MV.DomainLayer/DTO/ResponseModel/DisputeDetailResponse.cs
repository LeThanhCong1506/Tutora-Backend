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

    /// <summary>False khi chuỗi buổi (gốc + bù/phụ do gián đoạn + học lại do hoà giải) chứa buổi
    /// này đã đạt số buổi tối đa cho phép trong 1 chuỗi (xem
    /// DisputeRelearnPolicy.MaxRelearnSessionsPerChain) — CMS nên khoá lựa chọn "Học lại buổi
    /// này" trong modal đóng phản ánh, chỉ còn "Ra quyết định" (hoàn tiền) khả dụng.</summary>
    public bool RelearnAvailable { get; set; } = true;

    // Tutor rebuttal
    public string? TutorResponse { get; set; }
    public DateTime? TutorRespondedAt { get; set; }

    /// <summary>Phản hồi của phụ huynh/học sinh khi dispute do gia sư tạo (chiều ngược với <see cref="TutorResponse"/>).</summary>
    public string? RespondentResponse { get; set; }
    public DateTime? RespondentRespondedAt { get; set; }
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

    /// <summary>Trạng thái bản ghi video: available | processing | recording | failed | none.</summary>
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

    /// <summary>
    /// Toàn bộ chuỗi buổi liên kết (bù/phụ/học lại) chứa buổi bị tranh chấp, theo đúng thứ tự thời
    /// gian — đi hết cả chuỗi (đệ quy, không chỉ buổi gốc liền trước) vì 1 buổi phụ/buổi bù sau đó
    /// vẫn có thể bị tranh chấp và sinh ra buổi học lại của chính nó, tạo chuỗi dài hơn 2. Item có
    /// IsCurrent=true là đúng buổi đang bị tranh chấp trong phản ánh này.
    /// </summary>
    public List<ClassSessionRecordingChainItem> Chain { get; set; } = [];
}
