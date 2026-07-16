using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO;
using MV.DomainLayer.DTO.ResponseModel.Admin;
using MV.PresentationLayer.Authorization;

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
    /// Supports filtering by status, teachingMode, tutorId, parentId, subjectId, date range, and keyword search.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetBookings(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null,
        [FromQuery] string? teachingMode = null,
        [FromQuery] string? tutorId = null,
        [FromQuery] string? parentId = null,
        [FromQuery] int? subjectId = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] string? search = null,
        CancellationToken ct = default)
    {
        try
        {
            var validation = ValidatePagination(page, pageSize);
            if (validation != null) return validation;

            if (from.HasValue && to.HasValue && from > to)
                return BadRequest(APIResponse<object>.Fail("Ngày bắt đầu không được lớn hơn ngày kết thúc.", 400));

            var result = await adminBookingService.GetAdminBookingsAsync(
                page, pageSize, status, teachingMode,
                tutorId, parentId, subjectId,
                from, to, search, ct);

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
}
