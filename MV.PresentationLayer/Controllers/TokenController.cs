using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.DTO;
using MV.DomainLayer.DTO.RequestModel;

namespace MV.PresentationLayer.Controllers
{
    [Route("api/tokens")]
    [ApiController]
    public class TokenController : ControllerBase
    {
        private readonly IRefreshTokenService _refreshTokenService;

        public TokenController(IRefreshTokenService refreshTokenService)
        {
            _refreshTokenService = refreshTokenService;
        }

        /// <summary>
        /// Dùng refresh token để lấy access token mới (không cần [Authorize])
        /// </summary>
        [HttpPost("refresh")]
        public async Task<IActionResult> Refresh([FromBody] RefreshTokenRequest request)
        {
            if (string.IsNullOrEmpty(request.AccessToken) || string.IsNullOrEmpty(request.RefreshToken))
            {
                return BadRequest(new APIResponse<object>(400, "AccessToken và RefreshToken không được để trống.", null));
            }

            var result = await _refreshTokenService.RefreshAsync(request.AccessToken, request.RefreshToken);

            if (!string.IsNullOrEmpty(result.ErrorMessage))
            {
                return Unauthorized(new APIResponse<object>(401, result.ErrorMessage, null));
            }

            return Ok(new APIResponse<object>(200, "Refresh token thành công.", new
            {
                token = result.AccessToken,
                refreshToken = result.RefreshToken
            }));
        }

        /// <summary>
        /// Thu hồi refresh token khi logout
        /// </summary>
        [Authorize]
        [HttpPost("revoke")]
        public async Task<IActionResult> Revoke([FromBody] RevokeTokenRequest request)
        {
            if (string.IsNullOrEmpty(request.RefreshToken))
            {
                return BadRequest(new APIResponse<object>(400, "RefreshToken không được để trống.", null));
            }

            await _refreshTokenService.RevokeAsync(request.RefreshToken);
            return Ok(new APIResponse<object>(200, "Đăng xuất thành công.", null));
        }
    }
}
