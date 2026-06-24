using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;

namespace MV.ApplicationLayer.ServiceInterfaces;

public interface ISocialRegistrationService
{
    Task<TokenResponse> BeginAsync(
        string provider,
        string providerUserId,
        string? email,
        string? fullName,
        string? avatarUrl,
        string? requestedRole);

    Task<TokenResponse> CompleteRegistrationAsync(CompleteSocialRegistrationRequest request);
    Task<TokenResponse> VerifyPhoneOtpAsync(VerifySocialPhoneOtpRequest request);
    Task<TokenResponse> ResendPhoneOtpAsync(ResendSocialPhoneOtpRequest request);
}
