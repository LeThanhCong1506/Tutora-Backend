using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO;
using MV.DomainLayer.DTO.ResponseModel.Admin;

namespace MV.PresentationLayer.Controllers;

[ApiController]
[Route("api/admin/dashboard")]
[Authorize(Roles = UserRole.Admin)]
public class AdminDashboardController(IAdminDashboardService dashboardService) : ControllerBase
{
    private IActionResult HandleException(Exception ex) => ex switch
    {
        ArgumentException => BadRequest(APIResponse<object>.Fail(ex.Message, 400)),
        _ => StatusCode(500, APIResponse<object>.Fail($"Lỗi hệ thống: {ex.Message}", 500))
    };

    /// <summary>
    /// GET /api/admin/dashboard/stats
    /// Trả về snapshot tổng quan nền tảng cho trang chủ admin.
    /// Không cần tham số — luôn phản ánh trạng thái hiện tại.
    /// </summary>
    [HttpGet("stats")]
    public async Task<IActionResult> GetStats(CancellationToken ct = default)
    {
        try
        {
            var result = await dashboardService.GetStatsAsync(ct);
            return Ok(APIResponse<AdminDashboardStatsResponse>.Success(result, "Lấy tổng quan hệ thống thành công."));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    /// <summary>
    /// GET /api/admin/dashboard/users
    /// Trả về thống kê tăng trưởng và phân bổ người dùng.
    /// </summary>
    [HttpGet("users")]
    public async Task<IActionResult> GetUserStats(
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        CancellationToken ct = default)
    {
        try
        {
            if (from.HasValue && to.HasValue && from > to)
                return BadRequest(APIResponse<object>.Fail("Ngày bắt đầu không được lớn hơn ngày kết thúc.", 400));

            var result = await dashboardService.GetUserStatsAsync(from, to, ct);
            return Ok(APIResponse<AdminUserStatsResponse>.Success(result, "Lấy thống kê người dùng thành công."));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    /// <summary>
    /// GET /api/admin/dashboard/tutor-performance
    /// Trả về báo cáo hiệu suất gia sư và mức độ hài lòng của phụ huynh.
    /// Query params: top (default 10, max 50), from, to
    /// </summary>
    [HttpGet("tutor-performance")]
    public async Task<IActionResult> GetTutorPerformance(
        [FromQuery] int top = 10,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        CancellationToken ct = default)
    {
        try
        {
            if (top < 1 || top > 50)
                return BadRequest(APIResponse<object>.Fail("Tham số top phải từ 1 đến 50.", 400));

            if (from.HasValue && to.HasValue && from > to)
                return BadRequest(APIResponse<object>.Fail("Ngày bắt đầu không được lớn hơn ngày kết thúc.", 400));

            var result = await dashboardService.GetTutorPerformanceAsync(top, from, to, ct);
            return Ok(APIResponse<AdminTutorPerformanceResponse>.Success(result, "Lấy báo cáo hiệu suất gia sư thành công."));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    /// <summary>
    /// GET /api/admin/dashboard/summary
    /// Trả về thẻ KPI tóm tắt cho panel đầu trang admin: GMV, doanh thu nền tảng (có %thay đổi so với kỳ trước),
    /// số lượng booking, và danh sách việc cần xử lý của admin.
    /// Query params:
    ///   from, to  — khoảng thời gian (UTC). Mặc định: 30 ngày gần nhất.
    ///   timezone  — tên timezone IANA để FE tham chiếu (mặc định Asia/Ho_Chi_Minh). from/to luôn được coi là UTC.
    /// </summary>
    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary(
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] string timezone = "Asia/Ho_Chi_Minh",
        CancellationToken ct = default)
    {
        try
        {
            if (from.HasValue && to.HasValue && from > to)
                return BadRequest(APIResponse<object>.Fail("Ngày bắt đầu không được lớn hơn ngày kết thúc.", 400));

            var result = await dashboardService.GetSummaryAsync(from, to, timezone, ct);
            return Ok(APIResponse<AdminDashboardSummaryResponse>.Success(result, "Lấy tóm tắt dashboard thành công."));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    /// <summary>
    /// GET /api/admin/dashboard/disputes
    /// Trả về thống kê tranh chấp bao gồm tổng quan, tài chính, phân loại và xu hướng theo tháng.
    /// Query params: from, to (default: 30 ngày gần nhất)
    /// </summary>
    [HttpGet("disputes")]
    public async Task<IActionResult> GetDisputeStats(
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        CancellationToken ct = default)
    {
        try
        {
            if (from.HasValue && to.HasValue && from > to)
                return BadRequest(APIResponse<object>.Fail("Ngày bắt đầu không được lớn hơn ngày kết thúc.", 400));

            var result = await dashboardService.GetDisputeStatsAsync(from, to, ct);
            return Ok(APIResponse<AdminDisputeStatsResponse>.Success(result, "Lấy thống kê tranh chấp thành công."));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }
}
