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
        /// User chưa verify phone sẽ nhận socialRegistrationToken để hoàn tất OTP,
        /// không gọi lại authorization code lần hai.
        /// </summary>
        [HttpPost("zalo")]
        public async Task<IActionResult> LoginWithZalo([FromBody] ZaloWebLoginRequest request)
        {
            if (!ModelState.IsValid)
                return BadRequest(new APIResponse<object>(400, "Dữ liệu không hợp lệ.", null));

            var result = await _zaloAuthService.LoginWithZaloCodeAsync(request);

            if (result.RequiresRoleSelection || result.RequiresPhoneInput)
            {
                return Ok(new
                {
                    requiresRoleSelection = result.RequiresRoleSelection,
                    requiresPhoneInput = result.RequiresPhoneInput,
                    socialRegistrationToken = result.SocialRegistrationToken,
                    phone = result.Phone,
                    message = result.RequiresRoleSelection
                        ? "Vui lòng chọn vai trò và nhập số điện thoại để hoàn tất đăng ký."
                        : "Vui lòng nhập số điện thoại để tiếp tục."
                });
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
