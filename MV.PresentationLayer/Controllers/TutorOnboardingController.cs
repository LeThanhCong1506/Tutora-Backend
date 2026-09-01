using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Exceptions;
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
        private readonly ITutorProfileUpdateStagingService _updateStaging;
        private readonly IUserService _userService;

        public TutorOnboardingController(
            ITutorVerificationService verificationService,
            ITutorService tutorService,
            ITutorProfileUpdateStagingService updateStaging,
            IUserService userService)
        {
            _verificationService = verificationService;
            _tutorService = tutorService;
            _updateStaging = updateStaging;
            _userService = userService;
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
        /// Lấy trạng thái nhận booking hiện tại của gia sư (không cache — luôn realtime,
        /// khác với GET {id}/profile bên TutorController vốn cache 20 phút).
        /// </summary>
        [HttpGet("{id}/profile/accepting-bookings")]
        public async Task<IActionResult> GetAcceptingBookings([FromRoute] string id)
        {
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (currentUserId != id)
                return StatusCode(403, APIResponse.Fail(ApiMessages.Forbidden, 403));

            var profile = await _tutorService.GetTutorProfileAsync(id);
            if (profile == null) return NotFound(APIResponse.Fail(ApiMessages.TutorProfileNotFound, 404));
            return Ok(APIResponse<object>.Success(new { accepting = profile.IsAcceptingBookings }, "Lấy trạng thái nhận booking thành công."));
        }

        /// <summary>
        /// Tutor tự bật/tắt nhận booking mới (ẩn khỏi marketplace khi tắt).
        /// </summary>
        [HttpPut("{id}/profile/accepting-bookings")]
        public async Task<IActionResult> SetAcceptingBookings([FromRoute] string id, [FromBody] SetAcceptingBookingsRequest request)
        {
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (currentUserId != id)
                return StatusCode(403, APIResponse.Fail("Bạn chỉ có thể cập nhật hồ sơ của chính mình.", 403));

            var ok = await _tutorService.SetAcceptingBookingsAsync(id, request.Accepting);
            if (!ok) return NotFound(APIResponse.Fail(ApiMessages.TutorProfileNotFound, 404));
            var msg = request.Accepting ? "Đã mở nhận booking." : "Đã tạm dừng nhận booking.";
            return Ok(APIResponse<object>.Success(new { accepting = request.Accepting }, msg));
        }

        /// <summary>
        /// Upload CCCD (citizen ID card) 2 mặt — mặt trước và mặt sau.
        /// Chấp nhận JPG, JPEG, PNG — tối đa 5MB mỗi ảnh.
        /// Gọi FPT.AI OCR trực tiếp bằng bytes, lưu PublicId (private) vào DB.
        /// </summary>
        [Authorize(Roles = UserRole.Tutor)]
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
                // Ảnh mờ/giả hoặc CCCD đã được dùng bởi tài khoản khác.
                return UnprocessableEntity(APIResponse.Fail(ex.Message, 422));
            }
            catch (ArgumentException ex)
            {
                return BadRequest(APIResponse.Fail(ex.Message, 400));
            }
        }

        /// <summary>
        /// Gia sư xác nhận áp dụng thông tin đọc được từ CCCD vào hồ sơ (họ tên, ngày sinh,
        /// giới tính, địa chỉ thường trú). Bước quét chỉ lưu ảnh + dữ liệu OCR; hồ sơ chỉ đổi
        /// sau khi gia sư xem màn hình đối chiếu và gọi endpoint này.
        /// </summary>
        [Authorize(Roles = UserRole.Tutor)]
        [HttpPost("{id}/profile/cccd/confirm")]
        public async Task<IActionResult> ConfirmCccdProfile([FromRoute] string id)
        {
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (currentUserId != id)
                return StatusCode(403, APIResponse.Fail("Bạn chỉ có thể cập nhật hồ sơ của chính mình.", 403));

            try
            {
                var result = await _tutorService.ConfirmCccdProfileAsync(id);
                return Ok(APIResponse<CccdProfileConfirmResponse>.Success(result, result.Message));
            }
            catch (InvalidOperationException ex)
            {
                // Chưa từng quét CCCD → không có gì để xác nhận.
                return UnprocessableEntity(APIResponse.Fail(ex.Message, 422));
            }
            catch (ArgumentException ex)
            {
                return BadRequest(APIResponse.Fail(ex.Message, 400));
            }
        }

        /// <summary>
        /// Gia sư tự xem lại ảnh CCCD mình đã upload — signed URL, hết hạn sau 15 phút.
        /// Chỉ xem được CCCD của chính mình (so khớp userId từ JWT, không nhận id người khác).
        /// </summary>
        [Authorize(Roles = UserRole.Tutor)]
        [HttpGet("{id}/profile/cccd")]
        public async Task<IActionResult> GetOwnCccd([FromRoute] string id)
        {
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (currentUserId != id)
                return StatusCode(403, APIResponse.Fail("Bạn chỉ có thể xem CCCD của chính mình.", 403));

            try
            {
                var result = await _userService.GetUserCccdUrlsAsync(id);
                return Ok(APIResponse<UserCccdUrlsResponse>.Success(result, "Lấy link xem CCCD thành công."));
            }
            catch (UserNotFoundException ex)
            {
                return NotFound(APIResponse<object>.Fail(ex.Message, 404));
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
                return BuildProfileUpdateResponse(result, "Cập nhật thông tin cơ bản thành công.");
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
                return BuildProfileUpdateResponse(result, "Cập nhật phần giới thiệu thành công.");
            }
            catch (ArgumentException ex)
            {
                return BadRequest(APIResponse.Fail(ex.Message, 400));
            }
        }

        /// <summary>
        /// Chuẩn hoá response cho các mục hồ sơ có thể bị "chờ Admin duyệt" thay vì lưu thẳng
        /// (Thông tin cơ bản / Giới thiệu — trang "Hồ sơ gia sư", xem RequiresApprovalForEdits ở
        /// TutorService). Luôn kèm cờ <c>pendingApproval</c> để FE phân biệt, tránh báo "đã lưu"
        /// khi thực ra thay đổi mới chỉ nằm trong hàng chờ duyệt, chưa áp dụng.
        ///
        /// Môn học &amp; giá dùng chung helper này nhưng LUÔN trả pendingApproval=false: đó là mục
        /// của trang "Thiết lập giảng dạy" (/tutor-portal/onboarding), gia sư sửa là áp dụng ngay.
        /// </summary>
        private IActionResult BuildProfileUpdateResponse(ProfileUpdateOutcome outcome, string appliedMessage)
        {
            return outcome switch
            {
                ProfileUpdateOutcome.NotFound => NotFound(APIResponse.Fail(ApiMessages.TutorProfileNotFound, 404)),
                ProfileUpdateOutcome.PendingApproval => Ok(APIResponse<object>.Success(
                    new { pendingApproval = true },
                    "Hồ sơ của bạn đã được duyệt trước đó nên thay đổi này cần Admin xác nhận lại. Yêu cầu đã được gửi và đang chờ duyệt.")),
                _ => Ok(APIResponse<object>.Success(new { pendingApproval = false }, appliedMessage)),
            };
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
        /// Trả về trạng thái hoàn thành 6 mục hồ sơ. FE dùng để hiển thị progress bar.
        /// Khi đủ 6/6 mục, profile tự động chuyển sang PendingApproval.
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
        /// GET /api/tutors/{id}/profile/pending-update
        /// Trả về bản chỉnh sửa hồ sơ đang chờ Admin duyệt của chính tutor (null nếu không có).
        /// </summary>
        [HttpGet("{id}/profile/pending-update")]
        public async Task<IActionResult> GetPendingProfileUpdate([FromRoute] string id)
        {
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (currentUserId != id)
                return StatusCode(403, APIResponse.Fail(ApiMessages.Forbidden, 403));

            try
            {
                var result = await _updateStaging.GetPendingUpdateAsync(id);
                return Ok(APIResponse<PendingTutorProfileUpdate?>.Success(result, "Lấy thông tin bản cập nhật đang chờ duyệt thành công."));
            }
            catch (ArgumentException ex)
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
        /// Get tutor pricing information (hourly rate, trial classSession price, allow negotiation)
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
        /// Update tutor pricing information (hourly rate: 50,000 - 2,000,000 VND, trial classSession price, allow negotiation)
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
                return BuildProfileUpdateResponse(result, "Cập nhật thông tin giá thành công.");
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
            catch (InvalidOperationException ex)
            {
                return Conflict(APIResponse.Fail(ex.Message, 409));
            }
        }

        [HttpPut("{id}/profile/packages/{packageId:int}")]
        public async Task<IActionResult> UpdatePackage([FromRoute] string id, [FromRoute] int packageId, [FromBody] CreateTutorPackageRequest request)
        {
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (currentUserId != id)
            {
                return StatusCode(403, APIResponse.Fail("Bạn chỉ có thể sửa package của chính mình.", 403));
            }

            try
            {
                var result = await _tutorService.UpdateTutorPackageAsync(id, packageId, request);
                if (result == null)
                {
                    return NotFound(APIResponse.Fail("Không tìm thấy package.", 404));
                }

                return Ok(APIResponse<TutorPackageResponse>.Success(result, "Cập nhật package thành công."));
            }
            catch (ArgumentException ex)
            {
                return BadRequest(APIResponse.Fail(ex.Message, 400));
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(APIResponse.Fail(ex.Message, 409));
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

            try
            {
                var result = await _tutorService.DeactivateTutorPackageAsync(id, packageId);
                if (!result)
                {
                    return NotFound(APIResponse.Fail("Không tìm thấy package.", 404));
                }

                return Ok(APIResponse.Success("Đã tắt package thành công."));
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(APIResponse.Fail(ex.Message, 409));
            }
        }

        /// <summary>
        /// Bật lại (hiện lại trên marketplace) một package đã bị tắt trước đó.
        /// </summary>
        [HttpPut("{id}/profile/packages/{packageId:int}/activate")]
        public async Task<IActionResult> ActivatePackage([FromRoute] string id, [FromRoute] int packageId)
        {
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (currentUserId != id)
            {
                return StatusCode(403, APIResponse.Fail("Bạn chỉ có thể bật package của chính mình.", 403));
            }

            try
            {
                var result = await _tutorService.ActivateTutorPackageAsync(id, packageId);
                if (!result)
                {
                    return NotFound(APIResponse.Fail("Không tìm thấy package.", 404));
                }

                return Ok(APIResponse.Success("Đã hiện lại package thành công."));
            }
            catch (InvalidOperationException ex)
            {
                // Bắt tại chỗ để giữ NGUYÊN message (vd "khung ... nằm ngoài lịch rảnh của bạn").
                // ExceptionHandlingMiddleware thay mọi InvalidOperationException bằng một câu chung
                // chung, nên endpoint nào không tự bắt thì người dùng không biết mình sai ở đâu.
                return Conflict(APIResponse.Fail(ex.Message, 409));
            }
        }

        /// <summary>
        /// Xóa vĩnh viễn một package đã ẩn. Yêu cầu package đang Isactive=false và chưa từng
        /// có booking nào (mới tạo/tạo nhầm). Trả 409 nếu package đang bật hoặc đã từng được đặt.
        /// </summary>
        [HttpDelete("{id}/profile/packages/{packageId:int}/permanent")]
        public async Task<IActionResult> DeletePackagePermanently([FromRoute] string id, [FromRoute] int packageId)
        {
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (currentUserId != id)
            {
                return StatusCode(403, APIResponse.Fail("Bạn chỉ có thể xóa package của chính mình.", 403));
            }

            try
            {
                var result = await _tutorService.DeleteTutorPackageAsync(id, packageId);
                if (!result)
                {
                    return NotFound(APIResponse.Fail("Không tìm thấy package.", 404));
                }

                return Ok(APIResponse.Success("Đã xóa vĩnh viễn package thành công."));
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(APIResponse.Fail(ex.Message, 409));
            }
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
