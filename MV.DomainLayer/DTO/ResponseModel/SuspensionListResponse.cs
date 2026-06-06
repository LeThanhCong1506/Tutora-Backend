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
    /// Time remaining until auto-unsuspend
    /// </summary>
    public string? TimeRemainingDisplay
    {
        get
        {
            if (!EndDate.HasValue || !IsActive.GetValueOrDefault()) return null;
            if (EndDate <= MV.DomainLayer.Helpers.VietnamTimeHelper.Now) return "Sắp gỡ";
            
            var remaining = EndDate.Value - MV.DomainLayer.Helpers.VietnamTimeHelper.Now;
            if (remaining.TotalDays >= 1)
                return $"{(int)remaining.TotalDays} ngày {remaining.Hours}h";
            if (remaining.TotalHours >= 1)
                return $"{(int)remaining.TotalHours}h {remaining.Minutes}m";
            return $"{remaining.Minutes}m";
        }
    }
    
    /// <summary>
    /// Display suspension type
    /// </summary>
    public string SuspensionTypeDisplay => SuspensionType switch
    {
        "hidden_1_week" => "🔇 Ẩn profile 1 tuần",
        "account_locked" => "🔒 Khóa tài khoản",
        _ => SuspensionType
    };
}
