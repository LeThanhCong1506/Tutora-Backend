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
        private readonly ITutorSuggestionService _tutorSuggestionService;
        private readonly IUserService _userService;

        public StudentController(
            IStudentService studentService,
            ITutorSuggestionService tutorSuggestionService,
            IUserService userService)
        {
            _studentService = studentService ?? throw new ArgumentNullException(nameof(studentService));
            _tutorSuggestionService = tutorSuggestionService
                ?? throw new ArgumentNullException(nameof(tutorSuggestionService));
            _userService = userService ?? throw new ArgumentNullException(nameof(userService));
        }

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
        /// Học sinh tự xem lại ảnh CCCD mình đã upload — signed URL, hết hạn sau 15 phút.
        /// Không nhận id từ client — luôn lấy đúng userId từ JWT của người gọi.
        /// </summary>
        [HttpGet("me/cccd")]
        public async Task<IActionResult> GetOwnCccd()
        {
            try
            {
                var result = await _userService.GetUserCccdUrlsAsync(GetStudentUserId());
                return Ok(APIResponse<UserCccdUrlsResponse>.Success(result, "Lấy link xem CCCD thành công."));
            }
            catch (UserNotFoundException)
            {
                return NotFound(APIResponse<object>.Fail("Không tìm thấy hồ sơ học sinh.", 404));
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
            catch (BadRequestException ex)
            {
                return BadRequest(APIResponse<object>.Fail(ex.Message, 400));
            }
        }

        /// <summary>
        /// Gợi ý gia sư dựa trên chương học sinh đang vướng trong phiên giải bài này.
        /// </remarks>
        [HttpGet("me/tutor-suggestions")]
        public async Task<IActionResult> GetTutorSuggestions(
            [FromQuery] Guid? sessionId, CancellationToken ct)
        {
            var result = await _tutorSuggestionService.GetForSessionAsync(GetStudentUserId(), sessionId, ct);
            return Ok(APIResponse<StudentTutorSuggestionResponse>.Success(result, "Lấy gợi ý gia sư thành công."));
        }

        /// <summary>
        /// Đánh giá một gia sư trong danh sách gợi ý.
        /// </summary>
        [HttpPost("me/tutor-suggestions/vote")]
        public async Task<IActionResult> VoteTutorSuggestion(
            [FromBody] TutorSuggestionVoteRequest request, CancellationToken ct)
        {
            if (!ModelState.IsValid)
                return BadRequest(APIResponse<object>.Fail(ApiMessages.InvalidInputData, 400));

            try
            {
                await _tutorSuggestionService.VoteTutorAsync(GetStudentUserId(), request, ct);
                return Ok(APIResponse<object>.Success(null!, "Cảm ơn bạn đã góp ý."));
            }
            catch (ArgumentException ex)
            {
                return BadRequest(APIResponse<object>.Fail(ex.Message, 400));
            }
        }

        /// <summary>
        /// Học sinh bật/tắt nhận gợi ý gia sư.
        /// </summary>
        [HttpPatch("me/preferences")]
        public async Task<IActionResult> UpdatePreferences(
            [FromBody] StudentPreferencesRequest request, CancellationToken ct)
        {
            try
            {
                await _tutorSuggestionService.SetUserPreferenceAsync(
                    GetStudentUserId(), request.TutorSuggestionEnabled, ct);
                return Ok(APIResponse<object>.Success(
                    new { request.TutorSuggestionEnabled }, "Cập nhật tuỳ chọn thành công."));
            }
            catch (KeyNotFoundException)
            {
                return NotFound(APIResponse<object>.Fail("Không tìm thấy hồ sơ học sinh.", 404));
            }
        }
    }
}
