namespace MV.DomainLayer.DTO.ResponseModel;

/// <summary>
/// Suspension list item for admin
/// </summary>
public class SuspensionListResponse
{
    public int SuspensionId { get; set; }
    public string? UserId { get; set; }
    public string? UserName { get; set; }
    public string? UserEmail { get; set; }
    
    public string SuspensionType { get; set; } = null!;
    public string? Reason { get; set; }
    
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public bool? IsActive { get; set; }
    
    public string? CreatedByName { get; set; }

    /// <summary>
    /// What this suspension did to the courses the user was still teaching — sessions cancelled,
    /// money returned to payers. Only populated on the response that *created* the suspension;
    /// null when listing existing ones.
    /// </summary>
    public SuspensionRefundImpactResponse? RefundImpact { get; set; }

    /// <summary>
    /// Time remaining until auto-unsuspend
    /// </summary>
    public string? TimeRemainingDisplay
    {
        get
        {
            if (!EndDate.HasValue || !IsActive.GetValueOrDefault()) return null;
            if (EndDate <= MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow) return "Sắp gỡ";
            
            var remaining = EndDate.Value - MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
            if (remaining.TotalDays >= 1)
                return $"{(int)remaining.TotalDays} ngày {remaining.Hours}h";
            if (remaining.TotalHours >= 1)
                return $"{(int)remaining.TotalHours}h {remaining.Minutes}m";
            return $"{remaining.Minutes}m";
        }
    }
    
    /// <summary>
    /// Two vocabularies reach this column: "temporary"/"permanent" from the auto-suspension rule
    /// and the CMS, plus "hidden_1_week"/"account_locked" left over from an older CMS build.
    /// All four have to render, or the history table shows a raw enum value.
    /// </summary>
    public string SuspensionTypeDisplay => SuspensionType switch
    {
        "temporary" => "Có thời hạn",
        "permanent" => "Vô thời hạn",
        "hidden_1_week" => "Ẩn hồ sơ",
        "account_locked" => "Khóa tài khoản",
        _ => SuspensionType
    };
}
