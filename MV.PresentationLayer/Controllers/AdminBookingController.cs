using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel.Admin;
using MV.PresentationLayer.Authorization;
using MV.PresentationLayer.Helpers;

namespace MV.PresentationLayer.Controllers;

[ApiController]
[Route("api/admin/bookings")]
[Authorize]
[RequirePermission(Permissions.BookingView)]
public class AdminBookingController(IAdminBookingService adminBookingService) : ControllerBase
{
    private IActionResult ValidatePagination(int page, int pageSize) =>
        page < 1 || pageSize is < 1 or > 100
            ? BadRequest(APIResponse<object>.Fail("Tham số phân trang không hợp lệ.", 400))
            : null!;

    /// <summary>
    /// GET /api/admin/bookings
    /// Returns a paginated list of all bookings across the platform.
    /// Supports filtering by status, teachingMode, tutorId, parentId, subjectId, date range,
    /// keyword search, bookingId, classSessionId, and ordering by creation time (sortDirection).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetBookings(
        [FromQuery] AdminBookingQueryRequest query,
        CancellationToken ct = default)
    {
        try
        {
            var validation = ValidatePagination(query.Page, query.PageSize);
            if (validation != null) return validation;

            if (query.From.HasValue && query.To.HasValue && query.From > query.To)
                return BadRequest(APIResponse<object>.Fail("Ngày bắt đầu không được lớn hơn ngày kết thúc.", 400));

            var result = await adminBookingService.GetAdminBookingsAsync(query, ct);

            return Ok(APIResponse<AdminBookingListResponse>.Success(result, "Lấy danh sách đặt lịch thành công."));
        }
        catch (Exception ex)
        {
            return StatusCode(500, APIResponse<object>.Fail($"Lỗi hệ thống: {ex.Message}", 500));
        }
    }

    /// <summary>
    /// GET /api/admin/bookings/{id}
    /// Returns full detail of a single booking. No ownership check — admin can view any booking.
    /// </summary>
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetBookingDetail(
        [FromRoute] int id,
        CancellationToken ct = default)
    {
        try
        {
            var result = await adminBookingService.GetAdminBookingDetailAsync(id, ct);

            return result is null
                ? NotFound(APIResponse<object>.Fail("Không tìm thấy đặt lịch.", 404))
                : Ok(APIResponse<AdminBookingDetailResponse>.Success(result, "Lấy chi tiết đặt lịch thành công."));
        }
        catch (Exception ex)
        {
            return StatusCode(500, APIResponse<object>.Fail($"Lỗi hệ thống: {ex.Message}", 500));
        }
    }

    /// <summary>
    /// POST /api/admin/bookings/{id}/cancel-ghost
    /// Staff hủy booking sau khi xác minh NGOÀI hệ thống (qua tổng đài) rằng phụ huynh đã "nghỉ
    /// ngang" — không còn tham gia/phản hồi. Giải ngân toàn bộ escrow còn lại (kể cả các buổi
    /// chưa dạy) cho gia sư. Không gắn với luồng dispute nào — gia sư không cần thao tác gì trên
    /// hệ thống, chỉ cần liên hệ tổng đài để staff xác minh và thực hiện thao tác này.
    /// </summary>
    [RequirePermission(Permissions.BookingCancel)]
    [HttpPost("{id:int}/cancel-ghost")]
    public async Task<IActionResult> CancelGhostBooking(
        [FromRoute] int id,
        [FromBody] AdminCancelGhostBookingRequest request,
        CancellationToken ct = default)
    {
        try
        {
            var adminId = UserHelper.GetUserId(User);
            var success = await adminBookingService.CancelGhostBookingAsync(id, adminId, request.Reason, ct);

            return success
                ? Ok(APIResponse.Success("Đã hủy booking và giải ngân toàn bộ số tiền còn lại cho gia sư."))
                : BadRequest(APIResponse.Fail(
                    "Không thể hủy booking này lúc này (không tồn tại, đã kết thúc, hoặc có buổi đang xử lý dở dang).",
                    400));
        }
        catch (Exception ex)
        {
            return StatusCode(500, APIResponse<object>.Fail($"Lỗi hệ thống: {ex.Message}", 500));
        }
    }
}
