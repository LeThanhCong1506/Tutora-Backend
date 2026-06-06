using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using System.Security.Claims;


namespace MV.PresentationLayer.Controllers
{
    [Route("api/push-tokens")]
    [ApiController]
    [Authorize]
    public class PushTokenController : ControllerBase
    {
        private readonly IPushTokenService _pushTokenService;
        private readonly ILogger<PushTokenController> _logger;

        public PushTokenController(IPushTokenService pushTokenService, ILogger<PushTokenController> logger)
        {
            _pushTokenService = pushTokenService;
            _logger = logger;
        }

        /// <summary>
        /// Save or update FCM push notification token for the authenticated user
        /// </summary>
        /// <param name="request">Request containing fcmToken</param>
        /// <returns>Success or error message</returns>
        [HttpPost]
        public async Task<IActionResult> SavePushToken([FromBody] SavePushTokenRequest request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(new { message = ApiMessages.InvalidRequestPayload, errors = ModelState });
            }

            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (string.IsNullOrEmpty(currentUserId))
            {
                return Unauthorized(new { message = "Không tìm thấy ID người dùng trong token." });
            }

            var result = await _pushTokenService.SavePushTokenAsync(currentUserId, request.FcmToken!);

            if (!result)
            {
                _logger.LogWarning("Failed to save push token for user {UserId}", currentUserId);
                return NotFound(new { message = $"Không tìm thấy người dùng với ID {currentUserId} hoặc không thể lưu token." });
            }

            return Ok(new { message = "Lưu token thông báo thành công.", userId = currentUserId });
        }

        /// <summary>
        /// Remove FCM push notification token for the authenticated user
        /// </summary>
        /// <param name="userId">User ID</param>
        /// <returns>Success or error message</returns>
        [HttpDelete("{id}")]
        public async Task<IActionResult> RemovePushToken(string id)
        {
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var userRole = User.FindFirstValue(ClaimTypes.Role);

            if (string.IsNullOrEmpty(currentUserId))
            {
                return Unauthorized(new { message = "Không tìm thấy ID người dùng trong token." });
            }

            if (userRole != UserRole.Admin && currentUserId != id)
            {
                return Forbid();
            }

            // Remove token by setting it to null/empty
            var result = await _pushTokenService.DeletePushTokenAsync(id, string.Empty);

            if (!result)
            {
                return NotFound(new { message = $"Không tìm thấy người dùng với ID {id}." });
            }

            return Ok(new { message = "Xóa token thông báo thành công.", id });
        }
    }
}
