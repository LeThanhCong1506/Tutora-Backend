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
using MV.DomainLayer.Helpers;

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
        /// Danh sách chứng chỉ gia sư — có filter status, tìm kiếm tên/email, sắp xếp và phân trang.
        /// Query: pageNumber, pageSize, searchTerm, status (pending_review|verified|rejected|all), orderBy
        /// </summary>
        [Authorize(Roles = UserRole.AdminOrStaff)]
        [HttpGet("certificates/pending")]
        public async Task<IActionResult> GetAdminCertificates([FromQuery] CertificateParameters parameters)
        {
            try
            {
                var result = await _tutorService.GetAdminCertificatesAsync(parameters);

                var metadata = new
                {
                    result.TotalCount,
                    result.PageSize,
                    result.CurrentPage,
                    result.TotalPages,
                    result.HasNext,
                    result.HasPrevious
                };

                Response.Headers.Append("X-Pagination", JsonSerializer.Serialize(metadata));

                return Ok(APIResponse<PagedList<PendingCertificateResponse>>.Success(
                    result,
                    $"Lấy danh sách chứng chỉ thành công. Tổng: {result.TotalCount} chứng chỉ."));
            }
            catch (Exception ex)
            {
                return StatusCode(500, APIResponse<object>.Fail(ApiMessages.GenericErrorPrefix + ex.Message, 500));
            }
        }

        /// <summary>
        /// GET /api/admin/tutors/{id}/cccd
        /// Admin xem ảnh CCCD của gia sư. Trả về signed URL (yêu cầu chữ ký backend để truy cập).
        /// Chỉ Admin mới được gọi (không áp dụng cho Staff).
        /// </summary>
        [Authorize(Roles = UserRole.Admin)]
        [HttpGet("tutors/{id}/cccd")]
        public async Task<IActionResult> GetTutorCccdUrls(string id)
        {
            try
            {
                var result = await _userService.GetTutorCccdUrlsAsync(id);
                return Ok(APIResponse<TutorCccdUrlsResponse>.Success(
                    result,
                    "Lấy link xem CCCD thành công."));
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

        /// <summary>
        /// GET /api/admin/tutor-profile-update-requests
        /// Danh sách bản chỉnh sửa hồ sơ (của tutor đã Active) đang chờ Admin duyệt.
        /// </summary>
        [Authorize(Roles = UserRole.AdminOrStaff)]
        [HttpGet("tutor-profile-update-requests")]
        public async Task<IActionResult> GetPendingProfileUpdateRequests()
        {
            try
            {
                var result = await _tutorService.GetPendingProfileUpdateRequestsAsync();
                return Ok(APIResponse<List<PendingProfileUpdateRequestResponse>>.Success(
                    result, "Lấy danh sách yêu cầu cập nhật hồ sơ thành công."));
            }
            catch (ArgumentException ex)
            {
                return BadRequest(APIResponse<object>.Fail(ex.Message, 400));
            }
            catch (Exception ex)
            {
                return StatusCode(500, APIResponse<object>.Fail(ApiMessages.GenericErrorPrefix + ex.Message, 500));
            }
        }

        /// <summary>
        /// GET /api/admin/tutor-profile-update-requests/{tutorId}
        /// Bản mới nhất hiện tại của request đang chờ duyệt cho 1 tutor. FE gọi ngay trước khi
        /// Duyệt/Từ chối để so với nội dung Admin đang xem trên màn hình — vì màn hình danh sách
        /// không tự cập nhật real-time khi Tutor nộp thêm thay đổi.
        /// </summary>
        [Authorize(Roles = UserRole.AdminOrStaff)]
        [HttpGet("tutor-profile-update-requests/{tutorId}")]
        public async Task<IActionResult> GetProfileUpdateRequestDetail(string tutorId)
        {
            try
            {
                var result = await _tutorService.GetProfileUpdateRequestDetailAsync(tutorId);
                if (result == null)
                {
                    return NotFound(APIResponse<object>.Fail(
                        "Yêu cầu cập nhật không còn tồn tại — có thể đã được xử lý trước đó.", 404));
                }
                return Ok(APIResponse<PendingProfileUpdateRequestResponse>.Success(result, "Lấy thông tin yêu cầu cập nhật thành công."));
            }
            catch (ArgumentException ex)
            {
                return BadRequest(APIResponse<object>.Fail(ex.Message, 400));
            }
            catch (Exception ex)
            {
                return StatusCode(500, APIResponse<object>.Fail(ApiMessages.GenericErrorPrefix + ex.Message, 500));
            }
        }

        /// <summary>
        /// PUT /api/admin/tutor-profile-update-requests/{tutorId}/review
        /// Admin duyệt hoặc từ chối bản chỉnh sửa hồ sơ đang chờ của 1 tutor.
        /// </summary>
        [Authorize(Roles = UserRole.AdminOrStaff)]
        [HttpPut("tutor-profile-update-requests/{tutorId}/review")]
        public async Task<IActionResult> ReviewProfileUpdateRequest(
            string tutorId,
            [FromBody] AdminReviewProfileUpdateRequest request)
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
                var result = await _tutorService.ReviewProfileUpdateRequestAsync(tutorId, request, adminId);
                var message = request.IsApproved
                    ? "Duyệt cập nhật hồ sơ thành công. Marketplace đã hiển thị thông tin mới."
                    : "Từ chối cập nhật hồ sơ thành công.";
                if (result.HasNewerPendingChanges)
                {
                    message += " Lưu ý: Tutor đã nộp thêm thay đổi mới trong lúc bạn xử lý — vui lòng kiểm tra lại trong danh sách chờ duyệt.";
                }
                return Ok(APIResponse<ReviewProfileUpdateResponse>.Success(result, message));
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(APIResponse<object>.Fail(ex.Message, 404));
            }
            catch (ArgumentException ex)
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
