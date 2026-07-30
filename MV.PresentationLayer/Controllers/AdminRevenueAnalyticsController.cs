using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO;
using MV.DomainLayer.DTO.ResponseModel.Admin;
using MV.PresentationLayer.Authorization;

namespace MV.PresentationLayer.Controllers;

/// <summary>
/// Báo cáo doanh thu cho CMS (/admin-portal/revenue-reports).
/// </summary>
[ApiController]
[Route("api/admin/revenue-reports")]
[Authorize]
[RequirePermission(Permissions.FinancialView)]
public class AdminRevenueAnalyticsController(
    IAdminRevenueAnalyticsService service) : ControllerBase
{
    private IActionResult HandleException(Exception ex) =>
        StatusCode(500, APIResponse<object>.Fail($"Lỗi hệ thống: {ex.Message}", 500));

    private IActionResult? ValidateRange(DateTime? from, DateTime? to) =>
        from.HasValue && to.HasValue && from > to
            ? BadRequest(APIResponse<object>.Fail("Ngày bắt đầu không được lớn hơn ngày kết thúc.", 400))
            : null;

    /// <summary>GET /api/admin/revenue-reports/overview</summary>
    [HttpGet("overview")]
    public async Task<IActionResult> GetOverview(
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        CancellationToken ct = default)
    {
        try
        {
            if (ValidateRange(from, to) is { } bad) return bad;
            var result = await service.GetOverviewAsync(from, to, ct);
            return Ok(APIResponse<AdminRevenueOverviewResponse>.Success(
                result, "Lấy tổng quan doanh thu thành công."));
        }
        catch (Exception ex) { return HandleException(ex); }
    }

    /// <summary>GET /api/admin/revenue-reports/recognition</summary>
    [HttpGet("recognition")]
    public async Task<IActionResult> GetRecognition(
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        CancellationToken ct = default)
    {
        try
        {
            if (ValidateRange(from, to) is { } bad) return bad;
            var result = await service.GetRecognitionAsync(from, to, ct);
            return Ok(APIResponse<AdminRevenueRecognitionResponse>.Success(
                result, "Lấy số liệu ghi nhận doanh thu thành công."));
        }
        catch (Exception ex) { return HandleException(ex); }
    }

    /// <summary>GET /api/admin/revenue-reports/tutors</summary>
    [HttpGet("tutors")]
    public async Task<IActionResult> GetTutors(
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] int top = 50,
        CancellationToken ct = default)
    {
        try
        {
            if (ValidateRange(from, to) is { } bad) return bad;
            if (top is < 1 or > 200)
                return BadRequest(APIResponse<object>.Fail("Tham số top phải trong khoảng 1-200.", 400));

            var result = await service.GetTutorRevenueAsync(from, to, top, ct);
            return Ok(APIResponse<AdminTutorRevenueResponse>.Success(
                result, "Lấy báo cáo doanh thu theo gia sư thành công."));
        }
        catch (Exception ex) { return HandleException(ex); }
    }

    /// <summary>GET /api/admin/revenue-reports/customers</summary>
    [HttpGet("customers")]
    public async Task<IActionResult> GetCustomers(
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] int top = 50,
        CancellationToken ct = default)
    {
        try
        {
            if (ValidateRange(from, to) is { } bad) return bad;
            if (top is < 1 or > 200)
                return BadRequest(APIResponse<object>.Fail("Tham số top phải trong khoảng 1-200.", 400));

            var result = await service.GetCustomerRevenueAsync(from, to, top, ct);
            return Ok(APIResponse<AdminCustomerRevenueResponse>.Success(
                result, "Lấy báo cáo doanh thu theo khách hàng thành công."));
        }
        catch (Exception ex) { return HandleException(ex); }
    }

    /// <summary>GET /api/admin/revenue-reports/subjects</summary>
    [HttpGet("subjects")]
    public async Task<IActionResult> GetSubjects(
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        CancellationToken ct = default)
    {
        try
        {
            if (ValidateRange(from, to) is { } bad) return bad;
            var result = await service.GetSubjectRevenueAsync(from, to, ct);
            return Ok(APIResponse<AdminSubjectRevenueResponse>.Success(
                result, "Lấy báo cáo doanh thu theo môn học thành công."));
        }
        catch (Exception ex) { return HandleException(ex); }
    }

    /// <summary>GET /api/admin/revenue-reports/ai</summary>
    [HttpGet("ai")]
    public async Task<IActionResult> GetAi(
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] int top = 20,
        CancellationToken ct = default)
    {
        try
        {
            if (ValidateRange(from, to) is { } bad) return bad;
            if (top is < 1 or > 200)
                return BadRequest(APIResponse<object>.Fail("Tham số top phải trong khoảng 1-200.", 400));

            var result = await service.GetAiRevenueAsync(from, to, top, ct);
            return Ok(APIResponse<AdminAiRevenueResponse>.Success(
                result, "Lấy báo cáo doanh thu AI thành công."));
        }
        catch (Exception ex) { return HandleException(ex); }
    }
}
