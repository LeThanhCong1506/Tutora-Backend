using MV.DomainLayer.DTO.ResponseModel;

namespace MV.ApplicationLayer.ServiceInterfaces;

public interface IBankListService
{
    Task<List<BankInfoResponse>> GetBankListAsync(CancellationToken cancellationToken = default);
}
