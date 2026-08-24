using System.ComponentModel.DataAnnotations;

namespace MV.DomainLayer.DTO.RequestModel;

/// <summary>
/// Request for admin to resolve a dispute
/// </summary>
public class ResolveDisputeRequest
{
    /// <summary>
    /// Resolution type: release, refund_50, refund_100, custom
    /// </summary>
    [Required(ErrorMessage = "Loại quyết định là bắt buộc")]
    public string ResolutionType { get; set; } = null!;

    /// <summary>
    /// Refund percentage to parent when ResolutionType = "custom" (0-100). Ignored otherwise —
    /// release/refund_50/refund_100 keep their fixed 0/50/100 mapping.
    /// </summary>
    [Range(0, 100, ErrorMessage = "Phần trăm hoàn tiền phải từ 0 đến 100")]
    public int? CustomRefundPercentage { get; set; }

    /// <summary>
    /// Note explaining the resolution decision
    /// </summary>
    [Required(ErrorMessage = "Ghi chú quyết định là bắt buộc")]
    [StringLength(2000, MinimumLength = 10, ErrorMessage = "Ghi chú phải từ 10 đến 2000 ký tự")]
    public string ResolutionNote { get; set; } = null!;

    /// <summary>
    /// Whether to create a warning for the tutor
    /// </summary>
    public bool CreateTutorWarning { get; set; } = false;

    /// <summary>
    /// Warning level if creating warning (1 or 2)
    /// </summary>
    [Range(1, 2, ErrorMessage = "Mức cảnh báo phải là 1 hoặc 2")]
    public int? WarningLevel { get; set; }
}

/// <summary>
/// Valid resolution types
/// </summary>
public static class ResolutionTypes
{
    /// <summary>
    /// 100% payment goes to tutor
    /// </summary>
    public const string Release = "release";

    /// <summary>
    /// 50% to tutor, 50% refund to parent
    /// </summary>
    public const string Refund50 = "refund_50";

    /// <summary>
    /// 100% refund to parent
    /// </summary>
    public const string Refund100 = "refund_100";

    /// <summary>
    /// Admin-entered arbitrary refund percentage — see <see cref="ResolveDisputeRequest.CustomRefundPercentage"/>.
    /// </summary>
    public const string Custom = "custom";

    /// <summary>
    /// Hủy toàn bộ các buổi CHƯA diễn ra còn lại của khóa học (đợt thanh toán 2 trở đi) và hoàn
    /// cho phụ huynh theo giá gốc, KHÔNG gồm 5% phí dịch vụ (khác buổi học thử). Buổi đang bị
    /// khiếu nại được settle bình thường như <see cref="Release"/> (gia sư giữ nguyên tiền buổi
    /// đó nếu đã dạy đủ) — không nằm trong phần hoàn tiền của lựa chọn này.
    /// </summary>
    public const string CancelCourse = "cancel_course";

    public static readonly string[] All = { Release, Refund50, Refund100, Custom, CancelCourse };
}
