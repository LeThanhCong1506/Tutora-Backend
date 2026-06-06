using MV.DomainLayer.DTO.ResponseModel;

namespace MV.ApplicationLayer.ServiceInterfaces
{
    public interface IRefreshTokenService
    {
        /// <summary>
        /// Issue a new access + refresh token pair by validating the expired access token
        /// and the current refresh token.
        /// </summary>
        Task<TokenResponse> RefreshAsync(string accessToken, string refreshToken);

        /// <summary>
        /// Revoke a refresh token (logout / token rotation).
        /// </summary>
        Task RevokeAsync(string refreshToken);
    }
}
