using MV.DomainLayer.DTO.BankVerification;

namespace MV.ApplicationLayer.Interfaces;

public interface IBankVerificationService
{
    Task<RequestVerifyResponse> RequestVerificationAsync(string userId, RequestVerifyRequest request, CancellationToken cancellationToken = default);
    Task<ConfirmVerifyResponse> ConfirmVerificationAsync(string userId, ConfirmVerifyRequest request, CancellationToken cancellationToken = default);
    Task<BankVerificationStatusResponse> GetVerificationStatusAsync(string userId, CancellationToken cancellationToken = default);
    Task<List<BankInfoResponse>> GetBankListAsync(CancellationToken cancellationToken = default);
}
