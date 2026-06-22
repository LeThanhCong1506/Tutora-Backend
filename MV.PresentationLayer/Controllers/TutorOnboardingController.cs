using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using System.Security.Claims;

namespace MV.PresentationLayer.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/tutors")]
    public class TutorOnboardingController : ControllerBase
    {
        private readonly ITutorVerificationService _verificationService;
        private readonly ITutorService _tutorService;

        public TutorOnboardingController(ITutorVerificationService verificationService, ITutorService tutorService)
        {
            _verificationService = verificationService;
            _tutorService = tutorService;
        }

        /// <summary>
        /// Cập nhật link video giới thiệu (YouTube)
        /// </summary>
        [HttpPut("{id}/profile/video")]
        public async Task<IActionResult> UpdateVideo([FromRoute] string id, [FromBody] UpdateTutorVideoRequest request)
        {
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (currentUserId != id)
            {
                return StatusCode(403, APIResponse.Fail(ApiMessages.Forbidden, 403));
            }

            if (!ModelState.IsValid)
            {
                return BadRequest(APIResponse<object>.Fail(ApiMessages.InvalidRequestData, 400, ModelState));
            }

            try
            {
                var result = await _tutorService.UpdateTutorVideoAsync(id, request);

                if (!result)
                {
                    return NotFound(APIResponse.Fail(ApiMessages.TutorProfileNotFound, 404));
                }

                return Ok(APIResponse.Success("Cập nhật video giới thiệu thành công."));
            }
            catch (ArgumentException ex)
            {
                return BadRequest(APIResponse.Fail(ex.Message, 400));
            }
        }

        /// <summary>
        /// Upload CCCD (citizen ID card) 2 mặt — mặt trước và mặt sau.
        /// Chấp nhận JPG, JPEG, PNG — tối đa 5MB mỗi ảnh.
        /// Gọi FPT.AI OCR trực tiếp bằng bytes, lưu PublicId (private) vào DB.
        /// </summary>
        [HttpPost("{id}/profile/cccd")]
        [RequestSizeLimit(10_485_760)]
        [RequestFormLimits(MultipartBodyLengthLimit = 10_485_760)]
        public async Task<IActionResult> UploadCccd([FromRoute] string id, [FromForm] UploadCccdRequest request)
        {
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (currentUserId != id)
                return StatusCode(403, APIResponse.Fail("Bạn chỉ có thể cập nhật hồ sơ của chính mình.", 403));

            try
            {
                var result = await _tutorService.UploadCccdImagesAsync(id, request);
                return Ok(APIResponse<CccdUploadResponse>.Success(result, "Upload ảnh CCCD thành công."));
            }
            catch (InvalidOperationException ex)
            {
                // Tên CCCD không khớp với hồ sơ
                return UnprocessableEntity(APIResponse.Fail(ex.Message, 422));
            }
            catch (ArgumentException ex)
            {
                return BadRequest(APIResponse.Fail(ex.Message, 400));
            }
        }

        /// <summary>
        /// Upload/Update tutor avatar (supports JPG, PNG up to 5MB)
        /// </summary>
        [HttpPut("{id}/profile/avatar")]
        public async Task<IActionResult> UpdateAvatar([FromRoute] string id, [FromForm] UpdateTutorAvatarRequest request)
        {
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (currentUserId != id)
            {
                return StatusCode(403, APIResponse.Fail("Bạn chỉ có thể cập nhật hồ sơ của chính mình.", 403));
            }

            try
            {
                var avatarUrl = await _tutorService.UpdateTutorAvatarAsync(id, request.AvatarFile);

                if (avatarUrl == null)
                {
                    return NotFound(APIResponse.Fail(ApiMessages.UserNotFound, 404));
                }

                return Ok(APIResponse<object>.Success(new { avatarUrl }, "Cập nhật ảnh đại diện thành công."));
            }
            catch (ArgumentException ex)
            {
                return BadRequest(APIResponse.Fail(ex.Message, 400));
            }
        }

        /// <summary>
        /// Update tutor basic info: headline, teaching area, mode, subjects and custom tags (use JSON body)
        /// </summary>
        [HttpPut("{id}/profile/basic-info")]
        public async Task<IActionResult> UpdateBasicInfo([FromRoute] string id, [FromBody] UpdateTutorBasicInfoRequest request)
        {
            // Check quyền chính chủ
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (currentUserId != id)
            {
                return StatusCode(403, APIResponse.Fail("Bạn chỉ có thể cập nhật hồ sơ của chính mình.", 403));
            }

            // Validate request và thu thập tất cả lỗi
            var errors = new List<string>();

            // Validate Headline
            if (string.IsNullOrWhiteSpace(request.Headline))
            {
                errors.Add("Tiêu đề là bắt buộc.");
            }
            else if (request.Headline.Length < 10 || request.Headline.Length > 200)
            {
                errors.Add("Tiêu đề phải từ 10-200 ký tự.");
            }

            // Validate TeachingAreaCity
            if (string.IsNullOrWhiteSpace(request.TeachingAreaCity))
            {
                errors.Add("Thành phố là bắt buộc.");
            }

            // Validate TeachingAreaDistrict
            if (string.IsNullOrWhiteSpace(request.TeachingAreaDistrict))
            {
                errors.Add("Quận/huyện là bắt buộc.");
            }



            // Return all validation errors if any
            if (errors.Any())
            {
                return BadRequest(new
                {
                    success = false,
                    statusCode = 400,
                    message = "Dữ liệu không hợp lệ.",
                    errors = errors
                });
            }

            try
            {
                var result = await _tutorService.UpdateTutorBasicInfoAsync(id, request);

                if (!result)
                {
                    return NotFound(APIResponse.Fail(ApiMessages.TutorProfileNotFound, 404));
                }

                return Ok(APIResponse.Success("Cập nhật thông tin cơ bản thành công."));
            }
            catch (ArgumentException ex)
            {
                return BadRequest(APIResponse.Fail(ex.Message, 400));
            }
        }

        /// <summary>
        /// Update tutor self-introduction: bio, education, GPA, and teaching experience
        /// </summary>
        [HttpPut("{id}/profile/introduction")]
        public async Task<IActionResult> UpdateIntroduction([FromRoute] string id, [FromBody] UpdateTutorIntroductionRequest request)
        {
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (currentUserId != id)
            {
                return StatusCode(403, APIResponse.Fail("Bạn chỉ có thể cập nhật hồ sơ của chính mình.", 403));
            }

            try
            {
                var result = await _tutorService.UpdateTutorIntroductionAsync(id, request);

                if (!result)
                {
                    return NotFound(APIResponse.Fail(ApiMessages.TutorProfileNotFound, 404));
                }

                return Ok(APIResponse.Success("Cập nhật phần giới thiệu thành công."));
            }
            catch (ArgumentException ex)
            {
                return BadRequest(APIResponse.Fail(ex.Message, 400));
            }
        }

        /// <summary>
        /// Add a new certificate for tutor - includes auto-check result
        /// If valid and all required fields completed → Profile auto-activated
        /// If invalid → FE should show popup for user to choose: re-upload or submit for admin review
        /// </summary>
        [HttpPost("{id}/profile/certificates")]
        public async Task<IActionResult> AddCertificate([FromRoute] string id, [FromForm] AddCertificateRequest request)
        {
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (currentUserId != id)
            {
                return StatusCode(403, APIResponse.Fail("Bạn chỉ có thể thêm chứng chỉ vào hồ sơ của chính mình.", 403));
            }

            try
            {
                var result = await _tutorService.AddCertificateAsync(id, request);
                return Ok(APIResponse<CertificateUploadResponse>.Success(
                    result, "Thêm chứng chỉ thành công. Chứng chỉ đang chờ admin xét duyệt."));
            }
            catch (ArgumentException ex)
            {
                return BadRequest(APIResponse.Fail(ex.Message, 400));
            }
        }

        /// <summary>
        /// GET /api/tutors/{id}/profile-completion
        /// Trả về trạng thái hoàn thành 6 mục hồ sơ. FE dùng để hiển thị progress bar
        /// và bật/tắt nút "Nộp hồ sơ".
        /// </summary>
        [HttpGet("{id}/profile-completion")]
        public async Task<IActionResult> GetProfileCompletion([FromRoute] string id)
        {
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (currentUserId != id)
                return StatusCode(403, APIResponse.Fail(ApiMessages.Forbidden, 403));

            var result = await _tutorService.GetProfileCompletionAsync(id);
            return Ok(APIResponse<ProfileCompletionResponse>.Success(result, "Lấy tiến trình hoàn thiện hồ sơ thành công."));
        }

        /// <summary>
        /// Submit profile for admin review — yêu cầu hoàn thành đủ 6/6 mục.
        /// </summary>
        [HttpPost("{id}/submit-for-review")]
        public async Task<IActionResult> SubmitForAdminReview([FromRoute] string id)
        {
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (currentUserId != id)
            {
                return StatusCode(403, APIResponse.Fail(ApiMessages.Forbidden, 403));
            }

            try
            {
                var result = await _tutorService.SubmitForAdminReviewAsync(id);

                if (!result)
                {
                    return NotFound(APIResponse.Fail(ApiMessages.TutorProfileNotFound, 404));
                }

                return Ok(APIResponse.Success("Đã gửi hồ sơ để admin xét duyệt. Trạng thái chuyển sang chờ phê duyệt."));
            }
            catch (Exception ex)
            {
                return BadRequest(APIResponse.Fail(ex.Message, 400));
            }
        }

        /// <summary>
        /// Get all certificates of a tutor
        /// </summary>
        [HttpGet("{id}/profile/certificates")]
        public async Task<IActionResult> GetCertificates([FromRoute] string id)
        {
            var result = await _tutorService.GetCertificatesAsync(id);
            return Ok(APIResponse<List<CertificateResponse>>.Success(result, "Lấy danh sách chứng chỉ thành công."));
        }

        /// <summary>
        /// Delete a certificate
        /// </summary>
        [HttpDelete("{id}/profile/certificates/{certId}")]
        public async Task<IActionResult> DeleteCertificate([FromRoute] string id, [FromRoute] string certId)
        {
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (currentUserId != id)
            {
                return StatusCode(403, APIResponse.Fail("Bạn chỉ có thể xóa chứng chỉ của chính mình.", 403));
            }

            try
            {
                var result = await _tutorService.DeleteCertificateAsync(id, certId);

                if (!result)
                {
                    return NotFound(APIResponse.Fail("Không tìm thấy chứng chỉ.", 404));
                }

                return Ok(APIResponse.Success("Xóa chứng chỉ thành công."));
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(403, APIResponse.Fail(ex.Message, 403));
            }
        }

        /// <summary>
        /// Get verification progress for a tutor - shows status of all sections
        /// </summary>
        [HttpGet("{id}/verification/progress")]
        public async Task<IActionResult> GetVerificationProgress([FromRoute] string id)
        {
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (currentUserId != id)
            {
                return StatusCode(403, APIResponse.Fail("Bạn chỉ có thể xem tiến trình xác minh của chính mình.", 403));
            }

            var result = await _verificationService.GetVerificationProgressAsync(id);

            if (result == null)
            {
                return NotFound(APIResponse.Fail(ApiMessages.UserNotFound, 404));
            }

            return Ok(APIResponse<VerificationProgressResponse>.Success(result, "Lấy tiến trình xác minh thành công."));
        }

        ///// <summary>
        ///// Get tutor profile preview for public display (cached, requires active profile status)
        ///// </summary>
        //[AllowAnonymous]
        //[HttpGet("{id}/preview")]
        //public async Task<IActionResult> GetTutorProfilePreview([FromRoute] string id)
        //{
        //    var result = await _verificationService.GetTutorProfilePreviewAsync(id);

        //    if (result == null)
        //    {
        //        return NotFound(APIResponse.Fail("Tutor profile not found or not approved.", 404));
        //    }

        //    return Ok(APIResponse<TutorProfilePreviewResponse>.Success(result, "Get tutor profile preview successfully."));
        //}

        // [DEPRECATED] POST verification/submit — đã thay thế bởi POST {id}/profile/cccd
        // Endpoint cũ nhận URL ảnh rồi download. Flow mới upload file trực tiếp + OCR trong 1 request.

        /// <summary>
        /// Get tutor pricing information (hourly rate, trial lesson price, allow negotiation)
        /// </summary>
        [HttpGet("{id}/profile/pricing")]
        public async Task<IActionResult> GetPricing([FromRoute] string id)
        {
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (currentUserId != id)
            {
                return StatusCode(403, APIResponse.Fail("Bạn chỉ có thể xem thông tin giá của chính mình.", 403));
            }

            var result = await _tutorService.GetTutorPricingAsync(id);

            if (result == null)
            {
                return NotFound(APIResponse.Fail(ApiMessages.TutorProfileNotFound, 404));
            }

            return Ok(APIResponse<TutorPricingResponse>.Success(result, "Lấy thông tin giá thành công."));
        }

        /// <summary>
        /// Update tutor pricing information (hourly rate: 50,000 - 2,000,000 VND, trial lesson price, allow negotiation)
        /// </summary>
        [HttpPut("{id}/profile/pricing")]
        public async Task<IActionResult> UpdatePricing([FromRoute] string id, [FromBody] UpdateTutorPricingRequest request)
        {
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (currentUserId != id)
            {
                return StatusCode(403, APIResponse.Fail("Bạn chỉ có thể cập nhật thông tin giá của chính mình.", 403));
            }

            try
            {
                var result = await _tutorService.UpdateTutorPricingAsync(id, request);

                if (!result)
                {
                    return NotFound(APIResponse.Fail(ApiMessages.TutorProfileNotFound, 404));
                }

                return Ok(APIResponse.Success("Cập nhật thông tin giá thành công."));
            }
            catch (ArgumentException ex)
            {
                return BadRequest(APIResponse.Fail(ex.Message, 400));
            }
        }

        /// <summary>
        /// Add a single subject-grade-price entry for tutor
        /// </summary>
        [HttpPost("{id}/profile/pricing")]
        public async Task<IActionResult> AddSubjectGradePrice([FromRoute] string id, [FromBody] TutorSubjectGradePriceRequest request)
        {
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (currentUserId != id)
            {
                return StatusCode(403, APIResponse.Fail("Bạn chỉ có thể thêm giá cho chính mình.", 403));
            }

            try
            {
                var result = await _tutorService.AddSubjectGradePriceAsync(id, request);
                return CreatedAtAction(nameof(GetPricing), new { id },
                    APIResponse<TutorSubjectGradePriceResponse>.Success(result, "Thêm giá môn học thành công.", 201));
            }
            catch (ArgumentException ex)
            {
                return BadRequest(APIResponse.Fail(ex.Message, 400));
            }
        }

        /// <summary>
        /// Delete a single subject-grade-price entry for tutor.
        /// Soft-deletes if the entry is referenced by existing bookings; otherwise hard-deletes.
        /// </summary>
        [HttpDelete("{id}/profile/pricing/{subjectId:int}/{gradeLevelId:int}")]
        public async Task<IActionResult> DeleteSubjectGradePrice(
            [FromRoute] string id,
            [FromRoute] int subjectId,
            [FromRoute] int gradeLevelId)
        {
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (currentUserId != id)
            {
                return StatusCode(403, APIResponse.Fail("Bạn chỉ có thể xóa giá của chính mình.", 403));
            }

            try
            {
                var result = await _tutorService.DeleteSubjectGradePriceAsync(id, subjectId, gradeLevelId);
                if (!result)
                {
                    return NotFound(APIResponse.Fail("Không tìm thấy giá môn học tương ứng.", 404));
                }

                return Ok(APIResponse.Success("Xóa giá môn học thành công."));
            }
            catch (ArgumentException ex)
            {
                return BadRequest(APIResponse.Fail(ex.Message, 400));
            }
        }

        [HttpGet("{id}/profile/packages")]
        public async Task<IActionResult> GetPackages([FromRoute] string id, [FromQuery] bool includeInactive = false)
        {
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (currentUserId != id)
            {
                return StatusCode(403, APIResponse.Fail("Bạn chỉ có thể xem package của chính mình.", 403));
            }

            var result = await _tutorService.GetTutorPackagesAsync(id, includeInactive);
            return Ok(APIResponse<List<TutorPackageResponse>>.Success(result, "Lấy danh sách package thành công."));
        }

        [HttpPost("{id}/profile/packages")]
        public async Task<IActionResult> CreatePackage([FromRoute] string id, [FromBody] CreateTutorPackageRequest request)
        {
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (currentUserId != id)
            {
                return StatusCode(403, APIResponse.Fail("Bạn chỉ có thể tạo package cho chính mình.", 403));
            }

            try
            {
                var result = await _tutorService.CreateTutorPackageAsync(id, request);
                if (result == null)
                {
                    return NotFound(APIResponse.Fail(ApiMessages.TutorProfileNotFound, 404));
                }

                return Ok(APIResponse<TutorPackageResponse>.Success(result, "Tạo package thành công."));
            }
            catch (ArgumentException ex)
            {
                return BadRequest(APIResponse.Fail(ex.Message, 400));
            }
        }

        [HttpDelete("{id}/profile/packages/{packageId:int}")]
        public async Task<IActionResult> DeactivatePackage([FromRoute] string id, [FromRoute] int packageId)
        {
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (currentUserId != id)
            {
                return StatusCode(403, APIResponse.Fail("Bạn chỉ có thể tắt package của chính mình.", 403));
            }

            var result = await _tutorService.DeactivateTutorPackageAsync(id, packageId);
            if (!result)
            {
                return NotFound(APIResponse.Fail("Không tìm thấy package.", 404));
            }

            return Ok(APIResponse.Success("Đã tắt package thành công."));
        }

        /// <summary>
        /// Update tutor status to pending (request approval)
        /// </summary>
        // [HttpPut("{id}/status/pending")]
        // public async Task<IActionResult> UpdateStatusToPending([FromRoute] string id)
        // {
        //     var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        //     if (currentUserId != id)
        //     {
        //         return StatusCode(403, APIResponse.Fail("Forbidden: You can only update your own status", 403));
        //     }

        //     try
        //     {
        //         var result = await _verificationService.UpdateTutorStatusToPendingAsync(id);

        //         if (!result)
        //         {
        //             return NotFound(APIResponse.Fail(ApiMessages.TutorProfileNotFound, 404));
        //         }

        //         return Ok(APIResponse.Success("Tutor status updated to pending approval successfully"));
        //     }
        //     catch (Exception ex)
        //     {
        //         return BadRequest(APIResponse.Fail(ex.Message, 400));
        //     }
        // }
    }

}
