using MV.DomainLayer.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.DTO;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Exceptions;
using System.Security.Claims;

namespace MV.PresentationLayer.Controllers
{
    [Route("api/students")]
    [ApiController]
    [Authorize(Roles = UserRole.Student)]
    public class StudentController : ControllerBase
    {
        private readonly IStudentService _studentService;
        private readonly IUserService _userService;

        public StudentController(IStudentService studentService, IUserService userService)
        {
            _studentService = studentService ?? throw new ArgumentNullException(nameof(studentService));
            _userService = userService ?? throw new ArgumentNullException(nameof(userService));
        }

        private string GetStudentUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

        /// <summary>
        /// Get student profile information
        /// </summary>
        [HttpGet("profile")]
        public async Task<ActionResult<APIResponse<UserResponse>>> GetStudentProfile()
        {
            try
            {
                var userId = GetStudentUserId();
                var user = await _userService.GetUserByIdAsync(userId);
                return Ok(APIResponse<UserResponse>.Success(user, "Lấy thông tin người dùng thành công."));
            }
            catch (UserNotFoundException)
            {
                return NotFound(APIResponse<object>.Fail(ApiMessages.UserNotFound, 404));
            }
        }

        /// <summary>
        /// Student checks if they are linked to a parent.
        /// </summary>
        [HttpGet("link-status")]
        public async Task<IActionResult> GetMyLinkStatus()
        {
            var result = await _studentService.GetLinkStatusAsync(GetStudentUserId());
            return Ok(APIResponse<StudentLinkStatusResponse>.Success(result, "Lấy trạng thái liên kết thành công."));
        }

        /// <summary>
        /// Student updates their own avatar.
        /// </summary>
        [HttpPut("me/avatar")]
        public async Task<IActionResult> UpdateMyAvatar([FromForm] AvatarUploadRequest request)
        {
            if (!ModelState.IsValid)
                return BadRequest(APIResponse<object>.Fail("Dữ liệu file không hợp lệ.", 400));

            try
            {
                var result = await _studentService.UpdateSelfAvatarAsync(GetStudentUserId(), request.AvatarFile);
                return Ok(APIResponse<StudentProfileResponse>.Success(result, "Cập nhật ảnh đại diện thành công."));
            }
            catch (StudentNotFoundException)
            {
                return NotFound(APIResponse<object>.Fail("Không tìm thấy hồ sơ học sinh.", 404));
            }
        }

        /// <summary>
        /// Student updates their own profile fields.
        /// </summary>
        [HttpPut("me")]
        public async Task<IActionResult> UpdateMyProfile([FromBody] UpdateStudentRequest request)
        {
            if (!ModelState.IsValid)
                return BadRequest(APIResponse<object>.Fail(ApiMessages.InvalidInputData, 400));

            try
            {
                var result = await _studentService.UpdateSelfProfileAsync(GetStudentUserId(), request);
                return Ok(APIResponse<StudentProfileResponse>.Success(result, "Cập nhật hồ sơ thành công."));
            }
            catch (InvalidBirthdateException ex)
            {
                return BadRequest(APIResponse<object>.Fail(ex.Message, 400));
            }
            catch (StudentNotFoundException)
            {
                return NotFound(APIResponse<object>.Fail("Không tìm thấy hồ sơ học sinh.", 404));
            }
        }

        /// <summary>
        /// Student self-links with a parent using a parent code.
        /// </summary>
        [HttpPost("self-link")]
        public async Task<IActionResult> StudentSelfLink([FromBody] StudentSelfLinkRequest request)
        {
            if (!ModelState.IsValid)
                return BadRequest(APIResponse<object>.Fail(ApiMessages.InvalidInputData, 400));

            try
            {
                var result = await _studentService.StudentSelfLinkAsync(request.ParentCode, GetStudentUserId());
                return Ok(APIResponse<StudentProfileResponse>.Success(result, "Liên kết với phụ huynh thành công."));
            }
            catch (ParentCodeNotFoundException)
            {
                return NotFound(APIResponse<object>.Fail("Mã phụ huynh không hợp lệ.", 404));
            }
            catch (ParentCodeExpiredException)
            {
                return BadRequest(APIResponse<object>.Fail("Mã phụ huynh đã hết hạn.", 400));
            }
            catch (StudentAlreadyHasParentException)
            {
                return Conflict(APIResponse<object>.Fail("Học sinh này đã có phụ huynh liên kết.", 409));
            }
            catch (StudentNotFoundException)
            {
                return NotFound(APIResponse<object>.Fail("Không tìm thấy hồ sơ học sinh. Vui lòng đăng ký trước.", 404));
            }
        }
    }
}
