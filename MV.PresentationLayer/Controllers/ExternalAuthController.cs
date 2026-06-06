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
        // Endpoint duy nhất để Đăng nhập hoặc Tự động Đăng ký bằng Google ID Token
        [HttpPost("google")]
        public async Task<IActionResult> GoogleAuth([FromBody] GoogleAuthRequest request)
        {
            if (string.IsNullOrEmpty(request.IdToken))
            {
                return BadRequest(new { message = "Google ID Token là bắt buộc." });
            }

            var result = await _loginService.AuthenticateGoogleUserAsync(request.IdToken, request.Role);

            if (!string.IsNullOrEmpty(result.ErrorMessage))
            {
                return BadRequest(new { message = result.ErrorMessage });
            }

            return Ok(result);
        }
    }
}
