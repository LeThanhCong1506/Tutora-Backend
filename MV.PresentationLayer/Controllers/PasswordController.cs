using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using System.Security.Claims;

namespace MV.PresentationLayer.Controllers
{
    [Route("api/passwords")]
    [ApiController]
    public class PasswordController : ControllerBase
    {
        private readonly IPasswordService _passwordService;

        public PasswordController(IPasswordService passwordService)
        {
            _passwordService = passwordService;
        }

        /// <summary>
        /// Change password cho user đã đăng nhập
        /// </summary>
        [Authorize]
        [HttpPut("change")]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(new { message = ApiMessages.InvalidRequestPayload });
            }

            // Lấy userId từ JWT token của user đang đăng nhập
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized(new { message = ApiMessages.UserNotAuthenticated });
            }

            var (success, error) = await _passwordService.ChangePasswordAsync(
                userId,
                request.OldPassword,
                request.NewPassword);

            if (!success)
            {
                return BadRequest(new { message = error });
            }

            return Ok(new { message = "Đổi mật khẩu thành công." });
        }

    }
}
