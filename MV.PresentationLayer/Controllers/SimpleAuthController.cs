using Microsoft.AspNetCore.Mvc;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.DTO;
using MV.DomainLayer.DTO.RequestModel;

namespace MV.PresentationLayer.Controllers
{
    [Route("api/auth")]
    [ApiController]
    public class SimpleAuthController : ControllerBase
    {
        private readonly ISimpleAuthService _simpleAuthService;

        public SimpleAuthController(ISimpleAuthService simpleAuthService)
        {
            _simpleAuthService = simpleAuthService;
        }

        /// <summary>
        /// Simple login without Supabase.
        /// </summary>
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] SimpleLoginRequest request)
        {
            var result = await _simpleAuthService.SimpleLoginAsync(request);

            if (!string.IsNullOrEmpty(result.ErrorMessage))
            {
                return BadRequest(new APIResponse<object>(400, result.ErrorMessage, null));
            }

            return Ok(new APIResponse<object>(200, "Dang nhap thanh cong.", new { token = result.AccessToken, refreshToken = result.RefreshToken }));
        }

        /// <summary>
        /// Simple register without Supabase. Email registrations require OTP verification before returning JWT tokens.
        /// </summary>
        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] SimpleRegisterRequest request)
        {
            var result = await _simpleAuthService.SimpleRegisterAsync(request);

            if (!string.IsNullOrEmpty(result.ErrorMessage))
            {
                return BadRequest(new APIResponse<object>(400, result.ErrorMessage, null));
            }

            return Ok(new APIResponse<object>(200, "Dang ky thanh cong.", new
            {
                token = result.AccessToken,
                refreshToken = result.RefreshToken,
                requiresEmailVerification = result.RequiresEmailVerification,
                email = result.Email
            }));
        }

        /// <summary>
        /// Verify email by OTP sent through SMTP.
        /// </summary>
        [HttpPost("verify-email")]
        public async Task<IActionResult> VerifyEmail([FromBody] VerifyOtpRequest request)
        {
            var result = await _simpleAuthService.VerifyEmailOtpAsync(request);

            if (!string.IsNullOrEmpty(result.ErrorMessage))
            {
                return BadRequest(new APIResponse<object>(400, result.ErrorMessage, null));
            }

            return Ok(new APIResponse<object>(200, "Xac thuc email thanh cong.", new { token = result.AccessToken, refreshToken = result.RefreshToken }));
        }

        /// <summary>
        /// Resend email verification OTP through SMTP.
        /// </summary>
        [HttpPost("resend-verification-email")]
        public async Task<IActionResult> ResendVerificationEmail([FromBody] ResendVerificationEmailRequest request)
        {
            var result = await _simpleAuthService.ResendVerificationEmailAsync(request);

            if (!string.IsNullOrEmpty(result.ErrorMessage))
            {
                return BadRequest(new APIResponse<object>(400, result.ErrorMessage, null));
            }

            return Ok(new APIResponse<object>(200, "Da gui lai ma xac thuc email.", new
            {
                requiresEmailVerification = result.RequiresEmailVerification,
                email = result.Email
            }));
        }
        /// <summary>
        /// Request a password reset email link.
        /// </summary>
        [HttpPost("forgot-password")]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request)
        {
            var result = await _simpleAuthService.ForgotPasswordAsync(request);

            if (!string.IsNullOrEmpty(result.ErrorMessage))
            {
                return BadRequest(new APIResponse<object>(400, result.ErrorMessage, null));
            }

            return Ok(new APIResponse<object>(200, "Đường link khôi phục mật khẩu đã được gửi vào email của bạn.", null));
        }

        /// <summary>
        /// Reset password using token from email link.
        /// </summary>
        [HttpPost("reset-password")]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
        {
            var result = await _simpleAuthService.ResetPasswordAsync(request);

            if (!string.IsNullOrEmpty(result.ErrorMessage))
            {
                return BadRequest(new APIResponse<object>(400, result.ErrorMessage, null));
            }

            return Ok(new APIResponse<object>(200, "Đổi mật khẩu thành công.", null));
        }
    }
}
