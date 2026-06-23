using Microsoft.AspNetCore.Mvc;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.DTO;
using MV.DomainLayer.DTO.RequestModel;

namespace MV.PresentationLayer.Controllers
{
    [Route("api/auth")]
    [ApiController]
    public class ZaloAuthController : ControllerBase
    {
        private readonly IZaloAuthService _zaloAuthService;

        public ZaloAuthController(IZaloAuthService zaloAuthService)
        {
            _zaloAuthService = zaloAuthService;
        }

        /// <summary>
        /// Đăng nhập bằng Zalo Login v4 (Web OAuth).
        /// FE redirect sang Zalo, nhận authorization code, gửi { code, codeVerifier, redirectUri }.
        /// Backend đổi code lấy access token, verify, tìm/tạo user, trả Tutora JWT.
        /// </summary>
        [HttpPost("zalo")]
        public async Task<IActionResult> LoginWithZalo([FromBody] ZaloWebLoginRequest request)
        {
            if (!ModelState.IsValid)
                return BadRequest(new APIResponse<object>(400, "Dữ liệu không hợp lệ.", null));

            var result = await _zaloAuthService.LoginWithZaloCodeAsync(request);

            // User mới chưa chọn vai trò → FE cần hiện màn chọn role rồi đăng nhập lại
            if (result.RequiresRoleSelection)
            {
                return Ok(new { requiresRoleSelection = true, message = result.ErrorMessage });
            }

            if (!string.IsNullOrEmpty(result.ErrorMessage))
                return BadRequest(new APIResponse<object>(400, result.ErrorMessage, null));

            return Ok(new APIResponse<object>(200, "Đăng nhập Zalo thành công.", new
            {
                token = result.AccessToken,
                refreshToken = result.RefreshToken
            }));
        }
    }
}
