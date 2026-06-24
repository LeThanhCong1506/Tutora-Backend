using MV.DomainLayer.DTO.ResponseModel;

namespace MV.ApplicationLayer.ServiceInterfaces
{
    public interface ILoginService
    {
        /// <summary>
        /// Validate a Google ID token. Users without a verified phone continue through
        /// the social registration token and phone OTP flow before receiving a JWT.
        /// </summary>
        Task<TokenResponse> AuthenticateGoogleUserAsync(string idToken, string? role);
    }
}
