using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;

namespace MV.ApplicationLayer.ServiceInterfaces
{
    public interface IZaloAuthService
    {
        /// <summary>
        /// Zalo Login v4 (Web OAuth): đổi authorization code lấy access token,
        /// lấy profile, tìm/tạo user, cấp Tutora JWT.
        /// </summary>
        Task<TokenResponse> LoginWithZaloCodeAsync(ZaloWebLoginRequest request);
    }
}
