using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;

namespace MV.ApplicationLayer.ServiceInterfaces
{
    /// <summary>
    /// Service đơn giản để test login/register nhanh
    /// </summary>
    public interface ISimpleAuthService
    {
        Task<TokenResponse> SimpleLoginAsync(SimpleLoginRequest request);
        Task<TokenResponse> SimpleRegisterAsync(SimpleRegisterRequest request);
        Task<TokenResponse> VerifyEmailOtpAsync(VerifyOtpRequest request);
        Task<TokenResponse> ResendVerificationEmailAsync(ResendVerificationEmailRequest request);
        Task<TokenResponse> ForgotPasswordAsync(ForgotPasswordRequest request);
        Task<TokenResponse> ResetPasswordAsync(ResetPasswordRequest request);
    }
}
