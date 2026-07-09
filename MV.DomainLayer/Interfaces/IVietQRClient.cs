using MV.DomainLayer.DTO.ResponseModel;

namespace MV.DomainLayer.Interfaces;

public interface IVietQRClient
{
    Task<List<BankInfoResponse>> GetBankListAsync(CancellationToken cancellationToken = default);
}
