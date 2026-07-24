using MV.DomainLayer.Constants;

namespace MV.DomainLayer.DTO.ResponseModel;

/// <summary>
/// Dispute list item for admin
/// </summary>
public class DisputeListResponse
{
    public int DisputeId { get; set; }
    public int? ClassSessionId { get; set; }
    public int? BookingId { get; set; }
    
    public string? DisputeType { get; set; }
    public string? Status { get; set; }
    public string? Reason { get; set; }
    
    public string? CreatedByName { get; set; }
    public string? TutorName { get; set; }
    
    public decimal? ClassSessionPrice { get; set; }
    public DateTime? CreatedAt { get; set; }
    
    /// <summary>
    /// Display type with icon
    /// </summary>
    public string DisputeTypeDisplay => DisputeType switch
    {
        DisputeTypes.NoShow  => "🚫 Vắng mặt",
        DisputeTypes.Quality => "⚠️ Chất lượng",
        DisputeTypes.Payment => "💰 Thanh toán",
        DisputeTypes.Other   => "❓ Khác",
        _ => DisputeType ?? "Không xác định"
    };
    
    /// <summary>
    /// Status with color
    /// </summary>
    public string StatusDisplay => Status switch
    {
        DisputeStatus.Pending => "Chờ xử lý",
        DisputeStatus.Investigating => "Đang điều tra",
        DisputeStatus.ConfirmedNoShow => "Đã xác nhận vắng mặt",
        DisputeStatus.Resolved => "Đã giải quyết",
        DisputeStatus.Closed => "Đã đóng",
        _ => Status ?? "Không xác định"
    };
    
    public string StatusColor => Status switch
    {
        DisputeStatus.Pending => "#F59E0B",
        DisputeStatus.Investigating => "#3B82F6",
        DisputeStatus.ConfirmedNoShow => "#10B981",
        DisputeStatus.Resolved => "#10B981",
        DisputeStatus.Closed => "#6B7280",
        _ => "#9CA3AF"
    };
}
