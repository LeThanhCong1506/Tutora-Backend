using Microsoft.AspNetCore.Mvc;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.DTO.RequestModel;

namespace MV.PresentationLayer.Controllers
{
    [Route("api/auth")]
    [ApiController]
    public class ExternalAuthController : ControllerBase
    {
        private readonly ILoginService _loginService;

        public ExternalAuthController(ILoginService loginService)
        {
            _loginService = loginService;
        }

        //------------------------------------- GOOGLE DIRECT FLOW -------------------------------------
        // Xác thực Google; user chưa verify phone sẽ tiếp tục bằng socialRegistrationToken.
        [HttpPost("google")]
        public async Task<IActionResult> GoogleAuth([FromBody] GoogleAuthRequest request)
        {
            if (string.IsNullOrEmpty(request.IdToken))
            {
                return BadRequest(new { message = "Google ID Token là bắt buộc." });
            }

            var result = await _loginService.AuthenticateGoogleUserAsync(request.IdToken);

            if (result.RequiresRoleSelection || result.RequiresPhoneInput)
            {
                return Ok(new
                {
                    requiresRoleSelection = result.RequiresRoleSelection,
                    requiresPhoneInput = result.RequiresPhoneInput,
                    socialRegistrationToken = result.SocialRegistrationToken,
                    email = result.Email,
                    phone = result.Phone,
                    message = result.RequiresRoleSelection
                        ? "Vui lòng chọn vai trò và nhập số điện thoại để hoàn tất đăng ký."
                        : "Vui lòng nhập số điện thoại để tiếp tục."
                });
            }

            if (!string.IsNullOrEmpty(result.ErrorMessage))
            {
                return BadRequest(new { message = result.ErrorMessage });
            }

            return Ok(result);
        }
    }
}
