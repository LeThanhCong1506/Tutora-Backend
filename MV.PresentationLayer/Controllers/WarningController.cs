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
    private readonly ISuspensionRefundService _suspensionRefundService;

    public WarningController(
        IWarningService warningService,
        ISuspensionRefundService suspensionRefundService)
    {
        _warningService = warningService;
        _suspensionRefundService = suspensionRefundService;
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
    /// Get the full suspension history for one user, including suspensions that
    /// already ended. GET /admin/warnings/suspensions only covers currently
    /// active ones across all users, so admin user detail needs this instead.
    /// </summary>
    [RequirePermission(Permissions.WarningView)]
    [HttpGet("users/{id}/suspensions")]
    public async Task<ActionResult<APIResponse<List<SuspensionListResponse>>>> GetUserSuspensions(string id)
    {
        var result = await _warningService.GetUserSuspensionsAsync(id);
        return Ok(APIResponse<List<SuspensionListResponse>>.Success(result, "Lấy lịch sử tạm ngưng thành công."));
    }

    /// <summary>
    /// What suspending this account would cost: which upcoming sessions get cancelled and how much
    /// goes back to whoever paid. Works for tutors, parents and students alike.
    /// Read-only — nothing moves until <c>ApplySuspension</c> is called.
    /// <paramref name="durationDays"/> mirrors the suspend form: 0 (or omitted) previews an
    /// indefinite suspension, which reaches every undelivered session.
    /// </summary>
    [RequirePermission(Permissions.SuspensionManage)]
    [HttpGet("users/{id}/suspension-impact")]
    public async Task<ActionResult<APIResponse<SuspensionRefundImpactResponse>>> PreviewSuspensionImpact(
        string id,
        [FromQuery] int durationDays = 0)
    {
        DateTime? endDate = durationDays > 0
            ? MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow.AddDays(durationDays)
            : null;

        var result = await _suspensionRefundService.PreviewCascadeAsync(id, endDate);
        return Ok(APIResponse<SuspensionRefundImpactResponse>.Success(result, "Lấy dự báo ảnh hưởng thành công."));
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

        var impact = result.RefundImpact;
        var message = impact is null || impact.BookingsAffected == 0
            ? "Áp dụng tạm ngưng tài khoản thành công."
            : $"Áp dụng tạm ngưng thành công. Đã hủy {impact.SessionsCancelled} buổi học thuộc "
            + $"{impact.BookingsAffected} khóa và hoàn {impact.TotalRefunded:N0}đ cho người học.";

        return Ok(APIResponse<SuspensionListResponse>.Success(result, message));
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
