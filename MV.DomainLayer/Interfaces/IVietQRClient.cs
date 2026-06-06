using MV.DomainLayer.DTO.BankVerification;

namespace MV.DomainLayer.Interfaces;

public interface IVietQRClient
{
    Task<List<BankInfoResponse>> GetBankListAsync(CancellationToken cancellationToken = default);
}
