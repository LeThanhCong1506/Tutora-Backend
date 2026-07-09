using MV.DomainLayer.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.DTO;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.PresentationLayer.Helpers;
using MV.PresentationLayer.Authorization;

namespace MV.PresentationLayer.Controllers;

/// <summary>
/// Controller for admin warning and suspension management - M3
/// </summary>
[ApiController]
[Route("api/admin/warnings")]
[Authorize]
public class WarningController : ControllerBase
{
    private readonly IWarningService _warningService;

    public WarningController(IWarningService warningService)
    {
        _warningService = warningService;
    }

    /// <summary>
    /// Create a warning for a user.
    /// WarningLevel: 1 = Thấp, 2 = Trung bình, 3 = Cao.
    /// Cao triggers immediate suspension; Thấp/Trung bình trigger suspension after 3 accumulated warnings in 30 days.
    /// </summary>
    [RequirePermission(Permissions.WarningCreate)]
    [HttpPost("users/{id}")]
    public async Task<ActionResult<APIResponse<WarningHistoryResponse>>> CreateWarning(
        string id,
        [FromBody] CreateWarningRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(APIResponse.Fail("Dữ liệu không hợp lệ. Mức cảnh báo phải là 1 (Thấp), 2 (Trung bình) hoặc 3 (Cao).", 400));

        var adminId = UserHelper.GetUserId(User);
        var result = await _warningService.CreateWarningAsync(id, request, adminId);
        return Ok(APIResponse<WarningHistoryResponse>.Success(result, "Tạo cảnh báo thành công."));
    }

    /// <summary>
    /// Get user warning summary
    /// </summary>
    [RequirePermission(Permissions.WarningView)]
    [HttpGet("users/{id}")]
    public async Task<ActionResult<APIResponse<UserWarningSummaryResponse>>> GetUserWarnings(string id)
    {
        var result = await _warningService.GetUserWarningsAsync(id);
        return Ok(APIResponse<UserWarningSummaryResponse>.Success(result, "Lấy danh sách cảnh báo thành công."));
    }

    /// <summary>
    /// Apply suspension to a user
    /// </summary>
    [RequirePermission(Permissions.SuspensionManage)]
    [HttpPost("users/{id}/suspend")]
    public async Task<ActionResult<APIResponse<SuspensionListResponse>>> ApplySuspension(
        string id,
        [FromBody] SuspensionRequest request)
    {
        var adminId = UserHelper.GetUserId(User);
        var result = await _warningService.CreateSuspensionAsync(
            id, request.SuspensionType, request.Reason, request.DurationDays ?? 7, adminId);
        return Ok(APIResponse<SuspensionListResponse>.Success(result, "Áp dụng tạm ngưng tài khoản thành công."));
    }

    /// <summary>
    /// Remove suspension from a user
    /// </summary>
    [RequirePermission(Permissions.SuspensionManage)]
    [HttpPut("users/{id}/unsuspend")]
    public async Task<ActionResult<APIResponse<bool>>> RemoveSuspension(string id)
    {
        var adminId = UserHelper.GetUserId(User);
        var result = await _warningService.UnsuspendUserAsync(id, adminId);
        return Ok(APIResponse<bool>.Success(result, "Gỡ tạm ngưng tài khoản thành công."));
    }

    /// <summary>
    /// Get all active suspensions
    /// </summary>
    [RequirePermission(Permissions.WarningView)]
    [HttpGet("suspensions")]
    public async Task<ActionResult<APIResponse<PagedList<SuspensionListResponse>>>> GetSuspensions(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10)
    {
        var result = await _warningService.GetActiveSuspensionsAsync(page, pageSize);
        return Ok(APIResponse<PagedList<SuspensionListResponse>>.Success(result, "Lấy danh sách tạm ngưng thành công."));
    }
}
