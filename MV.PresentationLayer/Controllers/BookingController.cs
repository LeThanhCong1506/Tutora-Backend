using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Exceptions;
using MV.PresentationLayer.Authorization;
using System.Security.Claims;

namespace MV.PresentationLayer.Controllers;

[ApiController]
[Route("api")]
[Authorize]
public class BookingController : ControllerBase
{
    private readonly IBookingService _bookingService;

    public BookingController(IBookingService bookingService)
    {
        _bookingService = bookingService;
    }

    private string? UserId => User.FindFirstValue(ClaimTypes.NameIdentifier);
    private bool IsParent => User.IsInRole(UserRole.Parent);
    private bool IsTutor => User.IsInRole(UserRole.Tutor);

    [HttpPost("bookings")]
    [Authorize(Roles = UserRole.ParentOrStudent)]
    public async Task<IActionResult> CreateBooking([FromBody] CreateBookingRequest dto)
    {
        var userId = UserId;
        var role = User.FindFirstValue(ClaimTypes.Role);
        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(role))
            return Unauthorized(APIResponse.Fail(ApiMessages.Unauthorized, 401));

        if (dto == null || !ModelState.IsValid)
            return BadRequest(new { errorCode = BookingErrorCodes.InvalidInput, message = "Dữ liệu đầu vào không hợp lệ.", errors = ModelState.IsValid ? null : ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).ToList() });

        try
        {
            var result = await _bookingService.CreateBookingAsync(userId, role, dto);
            return Ok(APIResponse<BookingResponse>.Success(result, "Đặt lịch thành công."));
        }
        catch (BookingException ex)
        {
            return StatusCode(ex.HttpStatus, new { errorCode = ex.ErrorCode, message = ex.Message });
        }
    }

    /// <summary>
    /// Trả về các khoảng thời gian đã được book của gia sư trong cửa sổ yêu cầu.
    /// FE dùng để disable/gray-out các slot không thể chọn trên form đặt lịch.
    /// </summary>
    [HttpGet("bookings/tutor/{tutorId}/booked-slots")]
    [Authorize(Roles = UserRole.ParentOrStudent)]
    public async Task<IActionResult> GetTutorBookedSlots(
        string tutorId,
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null)
    {
        var from = startDate ?? MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
        var to = endDate ?? from.AddMonths(2);
        if (to <= from)
            return BadRequest(APIResponse.Fail("endDate phải lớn hơn startDate.", 400));

        var slots = await _bookingService.GetTutorBookedSlotsAsync(tutorId, from, to);
        return Ok(APIResponse<List<BookedSlotResponse>>.Success(slots, "Lấy lịch đã đặt thành công."));
    }

    [HttpGet("parent/bookings")]
    [HttpGet("student/bookings")]
    [Authorize(Roles = UserRole.ParentOrStudent)]
    public async Task<IActionResult> GetMyBookings([FromQuery] int page = 1, [FromQuery] int pageSize = 10, [FromQuery] string? status = null)
    {
        var userId = UserId;
        var role = User.FindFirstValue(ClaimTypes.Role);
        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(role))
            return Unauthorized(APIResponse.Fail(ApiMessages.Unauthorized, 401));

        var result = await _bookingService.GetMyBookingsAsync(userId, role, page, pageSize, status);
        var payload = new
        {
            items = (List<BookingResponse>)result,
            totalCount = result.TotalCount,
            currentPage = result.CurrentPage,
            totalPages = result.TotalPages,
            pageSize = result.PageSize
        };
        return Ok(APIResponse<object>.Success(payload, ApiMessages.Success));
    }

    [HttpGet("bookings/{id}")]
    public async Task<IActionResult> GetBookingById([FromRoute] int id)
    {
        var userId = UserId;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized(APIResponse.Fail(ApiMessages.Unauthorized, 401));
        if (!IsParent && !IsTutor && !User.IsInRole(UserRole.Student))
            return Forbid();

        string role = IsParent ? UserRole.Parent : (IsTutor ? UserRole.Tutor : UserRole.Student);
        var result = await _bookingService.GetBookingByIdAsync(id, userId, role);
        if (result == null)
            return NotFound(APIResponse.Fail(ApiMessages.BookingNotFound, 404));
        return Ok(APIResponse<BookingResponse>.Success(result, ApiMessages.Success));
    }

    [HttpPost("bookings/{id}/apply-promotion")]
    [Authorize(Roles = UserRole.ParentOrStudent)]
    public async Task<IActionResult> ApplyPromotion([FromRoute] int id, [FromBody] ApplyPromotionRequest? request)
    {
        var userId = UserId;
        var role = User.FindFirstValue(ClaimTypes.Role);
        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(role))
            return Unauthorized(APIResponse.Fail(ApiMessages.Unauthorized, 401));
        if (request == null || !ModelState.IsValid)
            return BadRequest(new { errorCode = BookingErrorCodes.InvalidInput, message = "Dữ liệu đầu vào không hợp lệ." });

        try
        {
            var result = await _bookingService.ApplyPromotionAsync(id, userId, request.PromotionCode ?? "");
            if (result == null)
                return NotFound(APIResponse.Fail("Không tìm thấy đặt lịch hoặc không thể áp dụng khuyến mãi.", 404));
            return Ok(APIResponse<BookingResponse>.Success(result, "Áp dụng khuyến mãi thành công."));
        }
        catch (BookingException ex)
        {
            return StatusCode(ex.HttpStatus, new { errorCode = ex.ErrorCode, message = ex.Message });
        }
    }

    [HttpDelete("bookings/{id}")]
    [Authorize(Roles = UserRole.ParentOrStudentOrTutor)]
    public async Task<IActionResult> CancelBooking([FromRoute] int id, [FromQuery] string? reason = null)
    {
        var userId = UserId;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized(APIResponse.Fail(ApiMessages.Unauthorized, 401));

        var ok = await _bookingService.CancelBookingAsync(id, userId, reason);
        if (!ok)
            return BadRequest(APIResponse.Fail("Không thể hủy đặt lịch vào lúc này.", 400));
        return Ok(APIResponse.Success("Đặt lịch đã được hủy thành công."));
    }

    [HttpPost("bookings/{id}/finalize-early")]
    [Authorize(Roles = UserRole.ParentOrStudent)]
    public async Task<IActionResult> FinalizeBookingEarly(
        [FromRoute] int id,
        [FromQuery] string? reason = null,
        CancellationToken ct = default)
    {
        var userId = UserId;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized(APIResponse.Fail(ApiMessages.Unauthorized, 401));

        var ok = await _bookingService.FinalizeBookingEarlyByUserAsync(id, userId, reason, ct);
        if (!ok)
        {
            return Conflict(APIResponse.Fail(
                "Không thể kết thúc khóa học lúc này. Hãy tải lại để kiểm tra trạng thái thanh toán và buổi học.",
                409));
        }

        return Ok(APIResponse.Success("Khóa học đã được kết thúc. Các buổi chưa học đã được hủy."));
    }

    [HttpGet("promotions/validate")]
    [Authorize(Roles = UserRole.ParentOrStudent)]
    public async Task<IActionResult> ValidatePromotion([FromQuery] string? code, [FromQuery] decimal orderValue = 0)
    {
        var result = await _bookingService.ValidatePromotionAsync(code ?? "", orderValue);
        return Ok(APIResponse<PromotionValidateResponse>.Success(result, ApiMessages.Success));
    }

    [HttpPost("admin/promotions")]
    [RequirePermission(Permissions.PromotionManage)]
    public async Task<IActionResult> CreatePromotion([FromBody] CreatePromotionRequest? dto)
    {
        if (dto == null || !ModelState.IsValid)
            return BadRequest(new { errorCode = BookingErrorCodes.InvalidInput, message = "Dữ liệu đầu vào không hợp lệ." });
        try
        {
            var result = await _bookingService.CreatePromotionAsync(dto);
            return Ok(APIResponse<PromotionCreatedResponse>.Success(result, "Tạo khuyến mãi thành công."));
        }
        catch (BookingException ex)
        {
            return StatusCode(ex.HttpStatus, new { errorCode = ex.ErrorCode, message = ex.Message });
        }
    }

    /// <summary>
    /// PUT /api/admin/promotions/{id} — Admin: cập nhật thông tin mã giảm giá.
    /// Chỉ các field được gửi lên mới được cập nhật (PATCH semantics).
    /// Code không thể thay đổi sau khi tạo.
    /// </summary>
    [HttpPut("admin/promotions/{id:int}")]
    [RequirePermission(Permissions.PromotionManage)]
    public async Task<IActionResult> UpdatePromotion(
        [FromRoute] int id,
        [FromBody] UpdatePromotionRequest? dto,
        CancellationToken ct = default)
    {
        if (dto == null || !ModelState.IsValid)
            return BadRequest(APIResponse<object>.Fail("Dữ liệu đầu vào không hợp lệ.", 400));

        try
        {
            var result = await _bookingService.UpdatePromotionAsync(id, dto, ct);

            return result is null
                ? NotFound(APIResponse<object>.Fail("Không tìm thấy mã khuyến mãi.", 404))
                : Ok(APIResponse<PromotionResponse>.Success(result, "Cập nhật khuyến mãi thành công."));
        }
        catch (BookingException ex)
        {
            return StatusCode(ex.HttpStatus, new { errorCode = ex.ErrorCode, message = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, APIResponse<object>.Fail($"Lỗi hệ thống: {ex.Message}", 500));
        }
    }

    /// <summary>
    /// DELETE /api/admin/promotions/{id} — Admin: xóa mềm mã giảm giá (set isActive = false).
    /// Idempotent: nếu promotion đã inactive thì vẫn trả 200 kèm thông báo phù hợp.
    /// </summary>
    [HttpDelete("admin/promotions/{id:int}")]
    [RequirePermission(Permissions.PromotionManage)]
    public async Task<IActionResult> DeactivatePromotion(
        [FromRoute] int id,
        CancellationToken ct = default)
    {
        try
        {
            var (found, alreadyInactive) = await _bookingService.DeactivatePromotionAsync(id, ct);

            if (!found)
                return NotFound(APIResponse<object>.Fail("Không tìm thấy mã khuyến mãi.", 404));

            var message = alreadyInactive
                ? "Mã khuyến mãi này đã được tắt trước đó."
                : "Đã tắt mã khuyến mãi thành công.";

            return Ok(APIResponse<object>.Success(null, message));
        }
        catch (Exception ex)
        {
            return StatusCode(500, APIResponse<object>.Fail($"Lỗi hệ thống: {ex.Message}", 500));
        }
    }

    /// <summary>
    /// GET /api/admin/promotions — Admin: danh sách tất cả mã giảm giá.
    /// Query: page, pageSize, isActive (true/false), search (code hoặc description).
    /// </summary>
    [HttpGet("admin/promotions")]
    [RequirePermission(Permissions.PromotionManage)]
    public async Task<IActionResult> GetAllPromotions(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] bool? isActive = null,
        [FromQuery] string? search = null,
        CancellationToken ct = default)
    {
        if (page < 1 || pageSize is < 1 or > 100)
            return BadRequest(APIResponse<object>.Fail("Tham số phân trang không hợp lệ.", 400));

        try
        {
            var result = await _bookingService.GetAllPromotionsAsync(page, pageSize, isActive, search, ct);
            return Ok(APIResponse<PromotionListResponse>.Success(result, "Lấy danh sách khuyến mãi thành công."));
        }
        catch (Exception ex)
        {
            return StatusCode(500, APIResponse<object>.Fail($"Lỗi hệ thống: {ex.Message}", 500));
        }
    }

    /// <summary>
    /// POST /api/bookings/{id}/payment-otp/send — Gửi OTP xác thực giao dịch lớn (chỉ áp dụng
    /// cho booking của học sinh tự đăng ký) tới SĐT phụ huynh. `errorCode = PARENT_PHONE_REQUIRED`
    /// là tín hiệu để FE điều hướng người dùng sang trang hồ sơ thay vì hiện lỗi thông thường.
    /// </summary>
    [HttpPost("bookings/{id}/payment-otp/send")]
    [Authorize(Roles = UserRole.Student)]
    public async Task<IActionResult> SendPaymentOtp(int id, [FromBody] SendPaymentOtpRequest request)
    {
        var userId = UserId;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized(APIResponse.Fail(ApiMessages.Unauthorized, 401));

        try
        {
            await _bookingService.SendPaymentOtpAsync(id, userId, request.Phase);
            return Ok(APIResponse<object>.Success(new { }, "Đã gửi mã OTP tới số điện thoại phụ huynh."));
        }
        catch (ParentPhoneRequiredForLargeTransactionException ex)
        {
            return BadRequest(new { errorCode = "PARENT_PHONE_REQUIRED", message = ex.Message });
        }
        catch (OtpCooldownActiveException ex)
        {
            return BadRequest(new { errorCode = "OTP_COOLDOWN_ACTIVE", message = ex.Message, retryAfterSeconds = ex.RetryAfterSeconds });
        }
        catch (OtpDailyLimitExceededException ex)
        {
            return BadRequest(new { errorCode = "OTP_DAILY_LIMIT_EXCEEDED", message = ex.Message });
        }
        catch (BookingException ex)
        {
            return StatusCode(ex.HttpStatus, new { errorCode = ex.ErrorCode, message = ex.Message });
        }
    }

    /// <summary>
    /// POST /api/bookings/{id}/payment-otp/verify — Xác thực mã OTP giao dịch lớn. Đúng thì
    /// ghi cờ đã duyệt để BE cho phép trừ ví / tạo link PayOS cho booking + phase này.
    /// </summary>
    [HttpPost("bookings/{id}/payment-otp/verify")]
    [Authorize(Roles = UserRole.Student)]
    public async Task<IActionResult> VerifyPaymentOtp(int id, [FromBody] VerifyPaymentOtpRequest request)
    {
        var userId = UserId;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized(APIResponse.Fail(ApiMessages.Unauthorized, 401));

        try
        {
            await _bookingService.VerifyPaymentOtpAsync(id, userId, request.Phase, request.Code);
            return Ok(APIResponse<object>.Success(new { }, "Xác thực OTP thành công."));
        }
        catch (OtpExpiredException ex)
        {
            return BadRequest(new { errorCode = "OTP_EXPIRED", message = ex.Message });
        }
        catch (OtpInvalidException ex)
        {
            return BadRequest(new { errorCode = "OTP_INVALID", message = ex.Message });
        }
        catch (OtpTooManyAttemptsException ex)
        {
            return BadRequest(new { errorCode = "OTP_TOO_MANY_ATTEMPTS", message = ex.Message });
        }
        catch (BookingException ex)
        {
            return StatusCode(ex.HttpStatus, new { errorCode = ex.ErrorCode, message = ex.Message });
        }
    }
}
