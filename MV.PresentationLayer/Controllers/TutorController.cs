using MV.DomainLayer.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.DTO;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using System.Security.Claims;

namespace MV.PresentationLayer.Controllers
{
    [Route("api/tutors")]
    [ApiController]
    [Authorize]
    public class TutorController : ControllerBase
    {
        private readonly ITutorService _tutorService;
        private readonly IUserService _userService;
        private readonly ITutorVerificationService _verificationService;
        private readonly IFeedbackService _feedbackService;
        private readonly ITutorRecommendService _recommendService;

        public TutorController(
            ITutorService tutorService,
            IUserService userService,
            ITutorVerificationService verificationService,
            IFeedbackService feedbackService,
            ITutorRecommendService recommendService)
        {
            _tutorService = tutorService;
            _userService = userService;
            _verificationService = verificationService;
            _feedbackService = feedbackService;
            _recommendService = recommendService;
        }

        // AI-powered tutor recommendation.
        [AllowAnonymous]
        [HttpPost("recommend")]
        public async Task<IActionResult> RecommendTutors(
            [FromBody] TutorRecommendRequest request,
            CancellationToken cancellationToken)
        {
            var result = await _recommendService.RecommendAsync(request, cancellationToken);
            return Ok(APIResponse<TutorRecommendResponse>.Success(result, "Gợi ý gia sư thành công."));
        }

        // --- PUBLIC / GET ---
        [Authorize(Roles = UserRole.Tutor)]
        [HttpGet("me/profile")]
        public async Task<IActionResult> GetMyTutorProfileInfo()
        {
            var tutorId = GetCurrentUserId();
            if (string.IsNullOrEmpty(tutorId))
                return Unauthorized(APIResponse<object>.Fail(ApiMessages.Unauthorized, 401));

            var result = await _verificationService.GetTutorProfileInfoAsync(tutorId);

            if (result == null)
            {
                return NotFound(APIResponse.Fail("Không tìm thấy hồ sơ gia sư hoặc chưa được duyệt.", 404));
            }

            return Ok(APIResponse<TutorProfileInfoResponse>.Success(result, "Lấy thông tin hồ sơ gia sư thành công."));
        }

        [Authorize(Roles = UserRole.Tutor)]
        [HttpGet("me/schedule")]
        public async Task<IActionResult> GetMyTutorSchedule()
        {
            var tutorId = GetCurrentUserId();
            if (string.IsNullOrEmpty(tutorId))
                return Unauthorized(APIResponse<object>.Fail(ApiMessages.Unauthorized, 401));

            var result = await _verificationService.GetTutorScheduleAsync(tutorId);

            if (result == null)
            {
                return NotFound(APIResponse.Fail("Không tìm thấy hồ sơ gia sư hoặc chưa được duyệt.", 404));
            }

            return Ok(APIResponse<TutorScheduleResponse>.Success(result, "Lấy lịch gia sư thành công."));
        }

        [Authorize(Roles = UserRole.Tutor)]
        [HttpGet("me/feedbacks")]
        public async Task<IActionResult> GetMyTutorFeedbacks(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 10)
        {
            var tutorId = GetCurrentUserId();
            if (string.IsNullOrEmpty(tutorId))
                return Unauthorized(APIResponse<object>.Fail(ApiMessages.Unauthorized, 401));

            var result = await _feedbackService.GetTutorFeedbacksAsync(tutorId, page, pageSize);
            return Ok(APIResponse<PagedList<FeedbackListResponse>>.Success(result, "Lấy danh sách đánh giá gia sư thành công."));
        }

        /// <summary>
        /// Get full tutor profile for public display (cached 20 min)
        /// Includes all profile sections + schedule + feedbacks with statistics
        /// </summary>
        [AllowAnonymous]
        [HttpGet("{id}/profile")]
        public async Task<IActionResult> GetTutorProfileInfo([FromRoute] string id)
        {
            var result = await _verificationService.GetTutorProfileInfoAsync(id);

            if (result == null)
            {
                return NotFound(APIResponse.Fail("Không tìm thấy hồ sơ gia sư hoặc chưa được duyệt.", 404));
            }

            return Ok(APIResponse<TutorProfileInfoResponse>.Success(result, "Lấy thông tin hồ sơ gia sư thành công."));
        }

        [AllowAnonymous]
        [HttpGet("{id}/schedule")]
        public async Task<IActionResult> GetTutorSchedule([FromRoute] string id)
        {
            var result = await _verificationService.GetTutorScheduleAsync(id);

            if (result == null)
            {
                return NotFound(APIResponse.Fail("Không tìm thấy hồ sơ gia sư hoặc chưa được duyệt.", 404));
            }

            return Ok(APIResponse<TutorScheduleResponse>.Success(result, "Lấy lịch gia sư thành công."));
        }

        [AllowAnonymous]
        [HttpGet("{id}/feedbacks")]
        public async Task<IActionResult> GetTutorFeedbacks(
            [FromRoute] string id,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 10)
        {
            var result = await _feedbackService.GetTutorFeedbacksAsync(id, page, pageSize);
            return Ok(APIResponse<PagedList<FeedbackListResponse>>.Success(result, "Lấy danh sách đánh giá gia sư thành công."));
        }

        [AllowAnonymous]
        [HttpGet("{id}/full-profile")]
        public async Task<IActionResult> GetTutorFullProfile([FromRoute] string id)
        {
            var result = await _verificationService.GetTutorFullProfileAsync(id);

            if (result == null)
            {
                return NotFound(APIResponse.Fail("Không tìm thấy hồ sơ gia sư hoặc chưa được duyệt.", 404));
            }

            return Ok(APIResponse<TutorFullProfileResponse>.Success(result, "Lấy thông tin đầy đủ hồ sơ gia sư thành công."));
        }


        private string? GetCurrentUserId()
        {
            return User.FindFirstValue(ClaimTypes.NameIdentifier);
        }
    }
}
