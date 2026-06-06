using System.ComponentModel.DataAnnotations;

namespace MV.DomainLayer.DTO.RequestModel;

/// <summary>
/// Request for admin to resolve a dispute
/// </summary>
public class ResolveDisputeRequest
{
    /// <summary>
    /// Resolution type: release, refund_50, refund_100
    /// </summary>
    [Required(ErrorMessage = "Loại quyết định là bắt buộc")]
    public string ResolutionType { get; set; } = null!;

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

    public static readonly string[] All = { Release, Refund50, Refund100 };
}
