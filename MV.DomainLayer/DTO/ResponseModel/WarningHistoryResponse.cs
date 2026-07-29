namespace MV.DomainLayer.DTO.ResponseModel;

/// <summary>
/// Warning history for a user
/// </summary>
public class WarningHistoryResponse
{
    public int WarningId { get; set; }
    public int WarningLevel { get; set; }
    public string? Reason { get; set; }
    public int? RelatedBookingId { get; set; }
    public DateTime? CreatedAt { get; set; }

    public string? IssuedByName { get; set; }

    /// <summary>
    /// Display level with icon
    /// </summary>
    public string WarningLevelDisplay => WarningLevel switch
    {
        1 => "⚠️ Cảnh báo mức 1",
        2 => "🚨 Cảnh báo mức 2",
        3 => "⛔ Cảnh báo mức 3",
        _ => $"Mức {WarningLevel}"
    };

    public string WarningLevelColor => WarningLevel switch
    {
        1 => "#F59E0B",
        2 => "#EF4444",
        3 => "#B91C1C",
        _ => "#9CA3AF"
    };
}

/// <summary>
/// User warning summary
/// </summary>
public class UserWarningSummaryResponse
{
    public string? UserId { get; set; }
    public string? FullName { get; set; }
    public string? Email { get; set; }

    public int TotalWarnings { get; set; }
    public int Level1Warnings { get; set; }
    public int Level2Warnings { get; set; }
    public int WarningsLast30Days { get; set; }

    public bool IsSuspended { get; set; }
    public string? SuspensionType { get; set; }
    public DateTime? SuspensionEndDate { get; set; }

    public List<WarningHistoryResponse> Warnings { get; set; } = new();
}
