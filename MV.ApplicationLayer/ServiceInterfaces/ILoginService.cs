using MV.DomainLayer.DTO.ResponseModel;

namespace MV.ApplicationLayer.ServiceInterfaces
{
    public interface ILoginService
    {
        /// <summary>
        /// Validate a Google ID token. Auto registers if user doesn't exist, otherwise logs them in.
        /// </summary>
        Task<TokenResponse> AuthenticateGoogleUserAsync(string idToken, string? role);
    }
}
