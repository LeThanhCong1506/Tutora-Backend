using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;

namespace MV.ApplicationLayer.ServiceInterfaces
{
    public interface IZaloAuthService
    {
        /// <summary>
        /// Verify Zalo access token, find or create user, issue Tutora JWT.
        /// </summary>
        Task<TokenResponse> LoginWithZaloAsync(ZaloLoginRequest request);
    }
}
