using MV.DomainLayer.Constants;
using MV.DomainLayer.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.DTO;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.ApplicationLayer.Interfaces;
using MV.ApplicationLayer.RepositoryInterfaces;
using System.Security.Claims;

namespace MV.PresentationLayer.Controllers
{
    [Route("api/users")]
    [ApiController]
    public class UserController : ControllerBase
    {
        private readonly IUserService _userService;
        private readonly IUserRepository _userRepository;
        private readonly IAppDbContext _dbContext;
        private readonly ISupportMessageService _supportService;

        public UserController(IUserService userService, IUserRepository userRepository, IAppDbContext dbContext, ISupportMessageService supportService)
        {
            _userService = userService;
            _userRepository = userRepository;
            _dbContext = dbContext;
            _supportService = supportService;
        }

        [HttpGet("profile")]
        [Authorize]
        public async Task<IActionResult> GetUserProfile()
        {
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(currentUserId))
                return Unauthorized(APIResponse<object>.Fail(ApiMessages.Unauthorized, 401));

            try
            {
                var user = await _userService.GetUserByIdAsync(currentUserId);
                return Ok(APIResponse<UserResponse>.Success(user, "Lấy thông tin người dùng thành công."));
            }
            catch (UserNotFoundException)
            {
                return NotFound(APIResponse<object>.Fail(ApiMessages.UserNotFound, 404));
            }
        }

        [HttpGet("by-email/{email}")]
        [Authorize(Roles = UserRole.Admin)]
        public async Task<IActionResult> GetUserByEmail(string email)
        {
            var user = await _userRepository.GetUserByEmailAsync(email);
            if (user == null)
                return NotFound(APIResponse<object>.Fail(ApiMessages.UserNotFound, 404));
            var response = await _userService.GetUserByIdAsync(user.Userid);
            return Ok(APIResponse<UserResponse>.Success(response, "Lấy thông tin người dùng thành công."));
        }

        // Các endpoint nhân viên (GET/POST /api/staffs) đã tách sang StaffController
        // — POST /api/users tạo user đa-role trước đây cũng đã bỏ theo.

        [HttpPut("{id}")]
        [Authorize]
        public async Task<IActionResult> UpdateUser(string id, [FromBody] UpdateUserRequest request)
        {
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (currentUserId != id && !User.IsInRole(UserRole.Admin))
                return StatusCode(403, APIResponse<object>.Fail(ApiMessages.Forbidden, 403));

            try
            {
                await _userService.UpdateUserAsync(id, request);
                return Ok(APIResponse<object>.Success(null!, "Cập nhật thông tin thành công."));
            }
            catch (UserNotFoundException)
            {
                return NotFound(APIResponse<object>.Fail(ApiMessages.UserNotFound, 404));
            }
            catch (EmailAlreadyExistsException ex)
            {
                return BadRequest(APIResponse<object>.Fail(ex.Message, 400));
            }
        }

        //[HttpPut("{id}/tutor-profile")]
        //public async Task<IActionResult> UpdateTutorProfile(string id, [FromBody] UpdateTutorProfileRequest request)
        //{
        //    var currentUserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        //    if (User.IsInRole(UserRole.Tutor) && currentUserId != id)
        //    {
        //        return Forbid();
        //    }

        //    try
        //    {
        //        await _userService.UpdateTutorProfileAsync(id, request);

        //        return Ok(APIResponse<object>.Success(null, "Tutor profile updated successfully."));
        //    }
        //    catch (KeyNotFoundException ex)
        //    {
        //        return NotFound(APIResponse<object>.Fail(ex.Message, 404));
        //    }
        //    catch (Exception ex)
        //    {
        //        return BadRequest(APIResponse<object>.Fail(ex.Message, 400));
        //    }
        //}

        //[HttpPut("{id}/weekly-availability")]
        //[Authorize(Roles = "Tutor,Admin")]
        //public async Task<IActionResult> UpdateWeeklyAvailability(string id, [FromBody] UpdateTutorScheduleRequest request)
        //{
        //    var currentUserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        //    if (User.IsInRole(UserRole.Tutor) && currentUserId != id)
        //    {
        //        return Forbid();
        //    }

        //    try
        //    {
        //        await _userService.UpdateTutorWeeklyAvailabilityAsync(id, request);

        //        // --- TỰ ĐỘNG CẬP NHẬT TRẠNG THÁI ---
        //        await _userService.AutoUpdateTutorProfileStatusAsync(id);

        //        return Ok(APIResponse<object>.Success(null!, "Weekly schedule updated and profile status re-evaluated."));
        //    }
        //    catch (InvalidOperationException ex)
        //    {
        //        return BadRequest(APIResponse<object>.Fail(ex.Message, 400));
        //    }
        //    catch (KeyNotFoundException ex)
        //    {
        //        return NotFound(APIResponse<object>.Fail(ex.Message, 404));
        //    }
        //    catch (Exception ex)
        //    {
        //        return StatusCode(500, APIResponse<object>.Fail("Internal Server Error: " + ex.Message, 500));
        //    }
        //}

        /// <summary>
        /// Toggle Zalo notification preference for a user
        /// </summary>
        [HttpPatch("{id}/zalo-notify")]
        [Authorize]
        public async Task<IActionResult> UpdateZaloNotify(string id, [FromBody] UpdateZaloNotifyRequest request)
        {
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (currentUserId != id && !User.IsInRole(UserRole.Admin))
                return StatusCode(403, APIResponse<object>.Fail(ApiMessages.Forbidden, 403));

            var user = await _userRepository.GetUserByIdAsync(id);
            if (user == null)
                return NotFound(APIResponse<object>.Fail(ApiMessages.UserNotFound, 404));

            user.Zabornotifyenabled = request.ZaborNotifyEnabled;
            await _dbContext.SaveChangesAsync();

            return Ok(APIResponse<object>.Success(null!, "Cập nhật cài đặt thông báo Zalo thành công."));
        }

        [HttpDelete("{id}")]
        [Authorize(Roles = UserRole.Admin)]
        public async Task<IActionResult> DeleteUser(string id)
        {
            try
            {
                await _userService.DeleteUserAsync(id);
                return Ok(APIResponse<object>.Success(null!, "Xóa người dùng thành công."));
            }
            catch (UserNotFoundException)
            {
                return NotFound(APIResponse<object>.Fail(ApiMessages.UserNotFound, 404));
            }
        }

        /// <summary>
        /// Get onboarding tour completion status for the current user
        /// </summary>
        [HttpGet("tour-status")]
        [Authorize]
        public async Task<IActionResult> GetTourStatus()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(APIResponse<object>.Fail(ApiMessages.Unauthorized, 401));

            var user = await _userRepository.GetUserByIdAsync(userId);
            if (user == null)
                return NotFound(APIResponse<object>.Fail(ApiMessages.UserNotFound, 404));

            return Ok(APIResponse<object>.Success(new { hasCompletedTour = user.Hascompletedtour ?? false }, "Lấy trạng thái hướng dẫn thành công."));
        }

        /// <summary>
        /// Mark onboarding tour as completed for the current user
        /// </summary>
        [HttpPut("complete-tour")]
        [Authorize]
        public async Task<IActionResult> CompleteTour()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(APIResponse<object>.Fail(ApiMessages.Unauthorized, 401));

            var user = await _userRepository.GetUserByIdAsync(userId);
            if (user == null)
                return NotFound(APIResponse<object>.Fail(ApiMessages.UserNotFound, 404));

            user.Hascompletedtour = true;
            await _dbContext.SaveChangesAsync();

            return Ok(APIResponse<object>.Success(null!, "Đã đánh dấu hoàn thành hướng dẫn."));
        }

        /// <summary>
        /// Toggle tự khóa/mở tài khoản của chính người dùng.
        /// - Nếu đang hoạt động → tạm khóa (isdeactivated = true, lưu deactivatedat).
        /// - Nếu đang tạm khóa → mở lại  (isdeactivated = false, cập nhật deactivatedat).
        /// Tutor sẽ được ẩn/hiện hồ sơ tương ứng.
        /// </summary>
        [HttpPut("me/deactivate")]
        [Authorize]
        public async Task<IActionResult> ToggleDeactivation()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(APIResponse<object>.Fail(ApiMessages.Unauthorized, 401));

            try
            {
                var result = await _userService.ToggleDeactivationAsync(userId);
                return Ok(APIResponse<DeactivationStatusResponse>.Success(result, result.Message));
            }
            catch (UserNotFoundException)
            {
                return NotFound(APIResponse<object>.Fail(ApiMessages.UserNotFound, 404));
            }
        }

        /// <summary>
        /// Upload/update avatar for Parent, Student or Admin users
        /// </summary>
        [HttpPut("{id}/avatar")]
        [Authorize]
        public async Task<IActionResult> UpdateAvatar(string id, [FromForm] UpdateTutorAvatarRequest request)
        {
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (currentUserId != id && !User.IsInRole(UserRole.Admin))
                return StatusCode(403, APIResponse<object>.Fail("Bạn chỉ có thể cập nhật ảnh đại diện của chính mình.", 403));

            if (!ModelState.IsValid)
                return BadRequest(APIResponse<object>.Fail("Dữ liệu tệp không hợp lệ.", 400));

            try
            {
                var avatarUrl = await _userService.UpdateUserAvatarAsync(id, request.AvatarFile);
                return Ok(APIResponse<object>.Success(new { avatarUrl }, "Cập nhật ảnh đại diện thành công."));
            }
            catch (ArgumentException ex)
            {
                return BadRequest(APIResponse<object>.Fail(ex.Message, 400));
            }
        }

        /// <summary>
        /// My own support thread with Admin/Staff (tutor/parent/student side). Null content if no
        /// thread exists yet — the caller hasn't messaged support before.
        /// </summary>
        [HttpGet("me/support/thread")]
        [Authorize]
        public async Task<IActionResult> GetMySupportThread()
        {
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(currentUserId))
                return Unauthorized(APIResponse<object>.Fail(ApiMessages.Unauthorized, 401));

            var result = await _supportService.GetThreadForUserAsync(currentUserId);
            return Ok(APIResponse<SupportThreadDetailResponse?>.Success(result, "Lấy hội thoại hỗ trợ thành công."));
        }

        /// <summary>Sends a message to Admin/Staff, creating my support thread on first contact.</summary>
        [HttpPost("me/support/messages")]
        [Authorize]
        public async Task<IActionResult> SendMySupportMessage([FromBody] SendSupportMessageRequest request)
        {
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(currentUserId))
                return Unauthorized(APIResponse<object>.Fail(ApiMessages.Unauthorized, 401));

            var result = await _supportService.SendMessageAsUserAsync(currentUserId, request.Message);
            return Ok(APIResponse<SupportMessageItemResponse>.Success(result, "Gửi tin nhắn thành công."));
        }

        private const long MaxSupportImageSizeBytes = 5 * 1024 * 1024;

        /// <summary>Uploads and sends an image to Admin/Staff, creating my support thread on first contact.</summary>
        [HttpPost("me/support/images")]
        [Authorize]
        [RequestSizeLimit(MaxSupportImageSizeBytes)]
        public async Task<IActionResult> SendMySupportImage(IFormFile file)
        {
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(currentUserId))
                return Unauthorized(APIResponse<object>.Fail(ApiMessages.Unauthorized, 401));

            if (file == null || file.Length == 0)
                return BadRequest(APIResponse<object>.Fail("Không có file được chọn.", 400));
            if (file.Length > MaxSupportImageSizeBytes)
                return BadRequest(APIResponse<object>.Fail("Kích thước file vượt quá 5MB.", 400));
            if (!file.ContentType.StartsWith("image/"))
                return BadRequest(APIResponse<object>.Fail("Chỉ chấp nhận file hình ảnh.", 400));

            var result = await _supportService.SendImageAsUserAsync(currentUserId, file);
            return Ok(APIResponse<SupportMessageItemResponse>.Success(result, "Gửi hình ảnh thành công."));
        }
    }
}
