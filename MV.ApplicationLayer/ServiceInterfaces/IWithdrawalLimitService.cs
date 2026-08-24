using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel.Admin;

namespace MV.ApplicationLayer.ServiceInterfaces;

public interface IWithdrawalLimitService
{
    /// <summary>Đường nhanh cho WalletService/TutorFinanceService — chỉ đọc 1 dòng system_configs.</summary>
    Task<decimal> GetMinWithdrawalAmountAsync(CancellationToken ct = default);

    Task<AdminWithdrawalLimitResponse> AdminGetAsync(CancellationToken ct = default);

    Task<AdminWithdrawalLimitResponse> AdminSetAsync(
        AdminSetWithdrawalLimitRequest request, string? updatedByUserId, CancellationToken ct = default);
}
