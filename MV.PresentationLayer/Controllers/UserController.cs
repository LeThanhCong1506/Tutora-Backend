using MV.DomainLayer.Constants;
using MV.DomainLayer.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.DTO;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Entities;
using MV.ApplicationLayer.Interfaces;
using System.Security.Claims;

namespace MV.PresentationLayer.Controllers
{
    [Route("api/users")]
    [ApiController]
    public class UserController : ControllerBase
    {
        private readonly IUserService _userService;
        private readonly IUnitOfWork _unitOfWork;

        public UserController(IUserService userService, IUnitOfWork unitOfWork)
        {
            _userService = userService;
            _unitOfWork = unitOfWork;
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
                user.Ekycrawdata = null;
                user.Idcardfronturl = null;
                user.Idcardbackurl = null;
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
            var user = await _unitOfWork.UserRepository.GetUserByEmailAsync(email);
            if (user == null)
                return NotFound(APIResponse<object>.Fail(ApiMessages.UserNotFound, 404));
            return Ok(APIResponse<User>.Success(user, "Lấy thông tin người dùng thành công."));
        }

        [HttpGet("staffs")]
        [Authorize(Roles = UserRole.Admin)]
        public async Task<IActionResult> GetStaffs([FromQuery] UserParameters parameters)
        {
            var result = await _userService.GetUsersByRoleAsync(UserRole.Staff, parameters);
            return Ok(APIResponse<PagedList<UserResponse>>.Success(result, "Lấy danh sách nhân viên thành công."));
        }

        [HttpPost]
        [Authorize(Roles = UserRole.Admin)]
        public async Task<IActionResult> CreateUser([FromBody] CreateUserRequest request)
        {
            var createdUser = await _userService.CreateUserAsync(request);

            var response = APIResponse<UserResponse>.Success(createdUser, "Tạo tài khoản thành công.", 201);

            return StatusCode(201, response);
        }

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

            var user = await _unitOfWork.UserRepository.GetUserByIdAsync(id);
            if (user == null)
                return NotFound(APIResponse<object>.Fail(ApiMessages.UserNotFound, 404));

            user.Zabornotifyenabled = request.ZaborNotifyEnabled;
            await _unitOfWork.SaveChangesAsync();

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

            var user = await _unitOfWork.UserRepository.GetUserByIdAsync(userId);
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

            var user = await _unitOfWork.UserRepository.GetUserByIdAsync(userId);
            if (user == null)
                return NotFound(APIResponse<object>.Fail(ApiMessages.UserNotFound, 404));

            user.Hascompletedtour = true;
            await _unitOfWork.SaveChangesAsync();

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
        [AllowAnonymous]
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
    }
}
