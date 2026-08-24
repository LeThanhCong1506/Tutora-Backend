using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel.Admin;

namespace MV.ApplicationLayer.ServiceInterfaces;

public interface ICommissionConfigService
{
    /// <summary>Đường nhanh cho tính phí booking — chỉ đọc 2 dòng system_configs, không kèm lịch sử.
    /// Đơn vị: phân số (0.05 = 5%), khớp với BookingFeeCalculator.Calculate.</summary>
    Task<(decimal ParentPercent, decimal TutorPercent)> GetFeePercentsAsync(CancellationToken ct = default);

    Task<AdminCommissionConfigResponse> AdminGetAsync(CancellationToken ct = default);

    Task<AdminCommissionConfigResponse> AdminSetAsync(
        AdminSetCommissionConfigRequest request, string? updatedByUserId, CancellationToken ct = default);
}
