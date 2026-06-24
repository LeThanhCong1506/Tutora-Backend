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

            // Chưa xác thực số điện thoại → FE chuyển sang màn nhập OTP (không phải lỗi sai mật khẩu)
            if (result.RequiresPhoneVerification)
            {
                return Ok(new APIResponse<object>(200, result.ErrorMessage, new { requiresPhoneVerification = true, phone = result.Phone }));
            }

            if (!string.IsNullOrEmpty(result.ErrorMessage))
            {
                return BadRequest(new APIResponse<object>(400, result.ErrorMessage, null));
            }

            return Ok(new APIResponse<object>(200, "Dang nhap thanh cong.", new { token = result.AccessToken, refreshToken = result.RefreshToken }));
        }

        /// <summary>
        /// Đăng ký bằng số điện thoại + mật khẩu. Phải xác thực OTP phone trước khi nhận JWT.
        /// </summary>
        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] SimpleRegisterRequest request)
        {
            var result = await _simpleAuthService.SimpleRegisterAsync(request);

            if (!string.IsNullOrEmpty(result.ErrorMessage))
            {
                return BadRequest(new APIResponse<object>(400, result.ErrorMessage, null));
            }

            return Ok(new APIResponse<object>(200, "Đăng ký thành công. Vui lòng nhập mã OTP gửi tới số điện thoại để xác thực.", new
            {
                requiresPhoneVerification = result.RequiresPhoneVerification,
                phone = result.Phone
            }));
        }

        /// <summary>
        /// Xác thực số điện thoại bằng OTP. Thành công sẽ trả JWT.
        /// </summary>
        [HttpPost("verify-phone")]
        public async Task<IActionResult> VerifyPhone([FromBody] VerifyPhoneOtpRequest request)
        {
            var result = await _simpleAuthService.VerifyPhoneOtpAsync(request);

            if (!string.IsNullOrEmpty(result.ErrorMessage))
            {
                return BadRequest(new APIResponse<object>(400, result.ErrorMessage, null));
            }

            return Ok(new APIResponse<object>(200, "Xác thực số điện thoại thành công.", new { token = result.AccessToken, refreshToken = result.RefreshToken }));
        }

        /// <summary>
        /// Gửi lại mã OTP xác thực số điện thoại.
        /// </summary>
        [HttpPost("resend-phone-otp")]
        public async Task<IActionResult> ResendPhoneOtp([FromBody] ResendPhoneOtpRequest request)
        {
            var result = await _simpleAuthService.ResendPhoneOtpAsync(request);

            if (!string.IsNullOrEmpty(result.ErrorMessage))
            {
                return BadRequest(new APIResponse<object>(400, result.ErrorMessage, null));
            }

            return Ok(new APIResponse<object>(200, "Đã gửi lại mã OTP.", new
            {
                requiresPhoneVerification = result.RequiresPhoneVerification,
                phone = result.Phone
            }));
        }

        /// <summary>
        /// Quên mật khẩu: gửi mã OTP tới số điện thoại để đặt lại mật khẩu.
        /// </summary>
        [HttpPost("forgot-password")]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request)
        {
            var result = await _simpleAuthService.ForgotPasswordAsync(request);

            if (!string.IsNullOrEmpty(result.ErrorMessage))
            {
                return BadRequest(new APIResponse<object>(400, result.ErrorMessage, null));
            }

            // Luôn trả success kể cả khi số không tồn tại (tránh dò số điện thoại).
            return Ok(new APIResponse<object>(200, "Nếu số điện thoại tồn tại, mã OTP đặt lại mật khẩu đã được gửi.", new { phone = result.Phone }));
        }

        /// <summary>
        /// Đặt lại mật khẩu bằng OTP gửi tới số điện thoại.
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
