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

        public StudentController(IStudentService studentService)
            => _studentService = studentService ?? throw new ArgumentNullException(nameof(studentService));

        private string GetStudentUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier)!;


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
        /// Trạng thái đủ điều kiện đặt lịch của học sinh
        /// </summary>
        [HttpGet("me/booking-eligibility")]
        public async Task<IActionResult> GetBookingEligibility()
        {
            var result = await _studentService.GetBookingEligibilityAsync(GetStudentUserId());
            return Ok(APIResponse<StudentBookingEligibilityResponse>.Success(result, "Lấy trạng thái đặt lịch thành công."));
        }

        /// <summary>
        /// Học sinh tự đăng ký xác minh CCCD để chứng minh đủ 16 tuổi (bắt buộc trước khi đặt lịch).
        /// </summary>
        [HttpPost("me/verify-cccd")]
        public async Task<IActionResult> VerifyCccd([FromForm] UploadCccdRequest request)
        {
            if (!ModelState.IsValid)
                return BadRequest(APIResponse<object>.Fail("Dữ liệu ảnh CCCD không hợp lệ.", 400));

            try
            {
                var result = await _studentService.VerifyStudentCccdAsync(GetStudentUserId(), request);
                return Ok(APIResponse<CccdUploadResponse>.Success(result, result.Message));
            }
            catch (StudentNotFoundException)
            {
                return NotFound(APIResponse<object>.Fail("Không tìm thấy hồ sơ học sinh.", 404));
            }
            catch (ArgumentException ex)
            {
                return BadRequest(APIResponse<object>.Fail(ex.Message, 400));
            }
            catch (InvalidOperationException ex)
            {
                // Ảnh mờ/giả, tên không khớp, chưa đủ 16 tuổi, số CCCD trùng...
                return UnprocessableEntity(APIResponse<object>.Fail(ex.Message, 422));
            }
        }

        /// <summary>
        /// Học sinh tự đăng ký nhập/cập nhật SĐT phụ huynh (tùy chọn) để nhận ZNS theo dõi.
        /// </summary>
        [HttpPut("me/parent-phone")]
        public async Task<IActionResult> SetParentPhone([FromBody] SetParentPhoneRequest request)
        {
            if (!ModelState.IsValid)
                return BadRequest(APIResponse<object>.Fail(ApiMessages.InvalidInputData, 400));

            try
            {
                var saved = await _studentService.SetParentPhoneAsync(GetStudentUserId(), request.ParentPhone);
                return Ok(APIResponse<object>.Success(new { ParentPhone = saved }, "Cập nhật số điện thoại phụ huynh thành công."));
            }
            catch (StudentNotFoundException)
            {
                return NotFound(APIResponse<object>.Fail("Không tìm thấy hồ sơ học sinh.", 404));
            }
        }
    }
}
