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
        /// Đăng nhập bằng Zalo Mini App.
        /// Nhận Zalo access token, verify với Zalo API, tìm/tạo user, trả về Tutora JWT.
        /// </summary>
        [HttpPost("zalo/login")]
        public async Task<IActionResult> LoginWithZalo([FromBody] ZaloLoginRequest request)
        {
            if (string.IsNullOrEmpty(request.ZaloAccessToken))
                return BadRequest(APIResponse<object>.Fail("ZaloAccessToken không được để trống.", 400));

            var result = await _zaloAuthService.LoginWithZaloAsync(request);

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
