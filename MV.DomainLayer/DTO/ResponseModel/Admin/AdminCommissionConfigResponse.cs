namespace MV.DomainLayer.DTO.ResponseModel.Admin;

/// <summary>Cấu hình phí sàn hiện hành + lịch sử thay đổi. Đơn vị phần trăm: số nguyên (5 = 5%).</summary>
public class AdminCommissionConfigResponse
{
    public decimal ParentFeePercent { get; set; }
    public decimal TutorFeePercent { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedByName { get; set; }
    public List<AdminCommissionConfigHistoryItem> History { get; set; } = new();
}

public class AdminCommissionConfigHistoryItem
{
    public decimal ParentFeePercent { get; set; }
    public decimal TutorFeePercent { get; set; }
    public DateTime ChangedAt { get; set; }
    public string? ChangedByName { get; set; }
}
