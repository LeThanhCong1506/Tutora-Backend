using MV.DomainLayer.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.DTO;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;

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

        public TutorController(ITutorService tutorService, IUserService userService, ITutorVerificationService verificationService)
        {
            _tutorService = tutorService;
            _userService = userService;
            _verificationService = verificationService;
        }

        // --- PUBLIC / GET ---
        /// <summary>
        /// Get full tutor profile for public display (cached 20 min)
        /// Includes all profile sections + schedule + feedbacks with statistics
        /// </summary>
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

        [HttpGet("{id}")]
        public async Task<IActionResult> GetTutorProfile(string id)
        {
            var result = await _tutorService.GetTutorProfileAsync(id);

            if (result == null)
            {
                return NotFound(APIResponse<TutorProfileResponse>.Fail(ApiMessages.TutorProfileNotFound + ".", 404));
            }

            return Ok(APIResponse<TutorProfileResponse>.Success(result, "Lấy thông tin hồ sơ gia sư thành công."));
        }


        [HttpGet("subjects/{id}")]
        public async Task<IActionResult> GetTutorsBySubject(int id, [FromQuery] UserParameters parameters)
        {
            var result = await _userService.GetTutorsBySubjectAsync(id, parameters);
            return Ok(APIResponse<PagedList<UserResponse>>.Success(result, "Lấy danh sách gia sư theo môn học thành công."));
        }


        [HttpGet("{id}/profile")]
        public async Task<IActionResult> GetTutorProfileInfo(string id)
        {
            try
            {
                var result = await _userService.GetTutorProfileShortAsync(id);
                return Ok(APIResponse<TutorProfileShortResponse>.Success(result, "Lấy thông tin hồ sơ gia sư thành công."));
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(APIResponse<object>.Fail(ex.Message, 404));
            }
            catch (Exception ex)
            {
                return StatusCode(500, APIResponse<object>.Fail(ApiMessages.GenericErrorPrefix + ex.Message, 500));
            }
        }


        [Authorize(Roles = UserRole.Admin)]
        [HttpGet("pending")]
        public async Task<IActionResult> GetPendingTutors([FromQuery] UserParameters parameters)
        {
            var result = await _userService.GetPendingTutorsAsync(parameters);
            return Ok(APIResponse<PagedList<PendingTutorResponse>>.Success(result, "Lấy danh sách gia sư chờ duyệt thành công."));
        }
    }
}
