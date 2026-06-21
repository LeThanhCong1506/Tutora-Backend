using MV.DomainLayer.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.DTO;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Exceptions;
using System.Security.Claims;
using System.Text.Json;

namespace MV.PresentationLayer.Controllers
{
    [ApiController]
    [Route("api/admin")]
    [Authorize]
    public class AdminController : ControllerBase
    {
        private readonly IUserService _userService;
        private readonly ITutorService _tutorService;

        private string? AdminId => User.FindFirstValue(ClaimTypes.NameIdentifier);

        public AdminController(IUserService userService, ITutorService tutorService)
        {
            _userService = userService;
            _tutorService = tutorService;
        }

        /// <summary>
        /// GET /api/admin/tutors/pending
        /// Danh sách gia sư đang chờ admin duyệt hồ sơ (profilestatus = pending_approval).
        /// Trả về đầy đủ thông tin cá nhân + VerificationSections để admin review.
        /// </summary>
        [Authorize(Roles = UserRole.AdminOrStaff)]
        [HttpGet("tutors/pending")]
        public async Task<IActionResult> GetPendingTutors([FromQuery] UserParameters parameters)
        {
            try
            {
                var result = await _userService.GetPendingTutorsAsync(parameters);

                var metadata = new
                {
                    result.TotalCount,
                    result.PageSize,
                    result.CurrentPage,
                    result.TotalPages,
                    result.HasNext,
                    result.HasPrevious
                };

                Response.Headers.Add("X-Pagination", JsonSerializer.Serialize(metadata));

                return Ok(APIResponse<PagedList<PendingTutorResponse>>.Success(
                    result,
                    $"Lấy danh sách gia sư chờ duyệt thành công. Tổng: {result.TotalCount} hồ sơ."));
            }
            catch (Exception ex)
            {
                return StatusCode(500, APIResponse<object>.Fail(ApiMessages.GenericErrorPrefix + ex.Message, 500));
            }
        }

        /// <summary>
        /// PUT /api/admin/tutors/{id}/approval
        /// Duyệt hoặc từ chối hồ sơ gia sư.
        /// </summary>
        [Authorize(Roles = UserRole.AdminOrStaff)]
        [HttpPut("tutors/{id}/approval")]
        public async Task<IActionResult> ApproveTutor(string id, [FromBody] ApproveTutorRequest request)
        {
            if (!ModelState.IsValid)
                return BadRequest(APIResponse<object>.Fail(ApiMessages.InvalidInputData, 400));

            if (!request.IsApproved && string.IsNullOrWhiteSpace(request.Reason))
                return BadRequest(APIResponse<object>.Fail("Lý do từ chối là bắt buộc.", 400));

            var adminId = AdminId;
            if (string.IsNullOrEmpty(adminId))
                return Unauthorized(APIResponse<object>.Fail("Không xác định được admin.", 401));

            try
            {
                var result = await _userService.ApproveTutorProfileAsync(id, request, adminId);
                return Ok(APIResponse<ApproveTutorResponse>.Success(result, "Xử lý hồ sơ gia sư thành công."));
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(APIResponse<object>.Fail(ex.Message, 404));
            }
            catch (UserNotFoundException ex)
            {
                return NotFound(APIResponse<object>.Fail(ex.Message, 404));
            }
            catch (Exception ex)
            {
                return StatusCode(500, APIResponse<object>.Fail(ApiMessages.GenericErrorPrefix + ex.Message, 500));
            }
        }

        /// <summary>
        /// GET /api/admin/certificates/pending
        /// Danh sách tất cả chứng chỉ đang chờ admin xét duyệt.
        /// </summary>
        [Authorize(Roles = UserRole.AdminOrStaff)]
        [HttpGet("certificates/pending")]
        public async Task<IActionResult> GetPendingCertificates()
        {
            try
            {
                var result = await _tutorService.GetPendingCertificatesAsync();
                return Ok(APIResponse<List<PendingCertificateResponse>>.Success(
                    result,
                    $"Lấy danh sách chứng chỉ chờ duyệt thành công. Tổng: {result.Count} chứng chỉ."));
            }
            catch (Exception ex)
            {
                return StatusCode(500, APIResponse<object>.Fail(ApiMessages.GenericErrorPrefix + ex.Message, 500));
            }
        }

        /// <summary>
        /// PUT /api/admin/tutors/{tutorId}/certificates/{certId}/verify
        /// Admin duyệt hoặc từ chối một chứng chỉ của gia sư.
        /// </summary>
        [Authorize(Roles = UserRole.Admin)]
        [HttpPut("tutors/{tutorId}/certificates/{certId}/verify")]
        public async Task<IActionResult> VerifyCertificate(
            string tutorId,
            string certId,
            [FromBody] AdminVerifyCertificateRequest request)
        {
            if (!ModelState.IsValid)
                return BadRequest(APIResponse<object>.Fail(ApiMessages.InvalidInputData, 400));

            if (!request.IsApproved && string.IsNullOrWhiteSpace(request.Note))
                return BadRequest(APIResponse<object>.Fail("Lý do từ chối là bắt buộc.", 400));

            var adminId = AdminId;
            if (string.IsNullOrEmpty(adminId))
                return Unauthorized(APIResponse<object>.Fail("Không xác định được admin.", 401));

            try
            {
                var result = await _tutorService.AdminVerifyCertificateAsync(tutorId, certId, request, adminId);
                var message = request.IsApproved
                    ? (result.IsProfileActivated ? "Duyệt chứng chỉ thành công. Hồ sơ gia sư đã được kích hoạt." : "Duyệt chứng chỉ thành công.")
                    : "Từ chối chứng chỉ thành công.";
                return Ok(APIResponse<AdminVerifyCertificateResponse>.Success(result, message));
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(APIResponse<object>.Fail(ex.Message, 404));
            }
            catch (UnauthorizedAccessException ex)
            {
                return BadRequest(APIResponse<object>.Fail(ex.Message, 400));
            }
            catch (Exception ex)
            {
                return StatusCode(500, APIResponse<object>.Fail(ApiMessages.GenericErrorPrefix + ex.Message, 500));
            }
        }
    }
}
