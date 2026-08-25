namespace MV.DomainLayer.DTO.RequestModel;

/// <summary>Đặt phí sàn hai bên. Đơn vị: phần trăm nguyên (5 = 5%), không phải phân số.</summary>
public class AdminSetCommissionConfigRequest
{
    public decimal ParentFeePercent { get; set; }

    public decimal TutorFeePercent { get; set; }
}
