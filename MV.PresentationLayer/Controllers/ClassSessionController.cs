using MV.DomainLayer.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Exceptions;
using System.Security.Claims;

namespace MV.PresentationLayer.Controllers;

[ApiController]
[Route("api")]
public class ClassSessionController(IClassSessionService classSessionService) : ControllerBase
{
    private string? UserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    // Moved to TutorClassSessionController → GET api/tutor/classSessions
    // [HttpGet("tutor/class-sessions")]

    [HttpGet("parent/class-sessions")]
    [Authorize(Roles = UserRole.Parent)]
    public async Task<IActionResult> GetParentClassSessions(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] string? status = null)
    {
        var parentId = UserId ?? throw new UnauthorizedAccessException();
        var result = await classSessionService.GetParentClassSessionsAsync(parentId, page, pageSize, fromDate, status);
        return Ok(MV.DomainLayer.DTO.APIResponse<PagedList<ClassSessionResponse>>.Success(result, ApiMessages.Success));
    }

    [HttpGet("class-sessions/{id}")]
    [Authorize]
    public async Task<IActionResult> GetClassSessionById([FromRoute] int id)
    {
        var userId = UserId ?? string.Empty;
        var isParent = User.IsInRole(UserRole.Parent);
        var result = await classSessionService.GetClassSessionByIdAsync(id, userId, isParent);

        if (result == null)
            return NotFound(new { message = ApiMessages.ClassSessionNotFound });

        return Ok(MV.DomainLayer.DTO.APIResponse<ClassSessionResponse>.Success(result, ApiMessages.Success));
    }

    /// <summary>
    /// DELETE /api/class-sessions/{id}?reason=...
    /// KHÔNG hỗ trợ hủy từng buổi học lẻ (luôn trả 400) — việc giải phóng escrow đòi hỏi
    /// hủy toàn bộ booking. Endpoint giữ lại để client nhận thông báo hướng dẫn thay vì 404.
    /// </summary>
    [HttpDelete("class-sessions/{id:int}")]
    [Authorize(Roles = UserRole.ParentOrTutor)]
    public async Task<IActionResult> CancelClassSession([FromRoute] int id, [FromQuery] string? reason = null)
    {
        var userId = UserId ?? throw new UnauthorizedAccessException();
        var role = User.IsInRole(UserRole.Tutor) ? UserRole.Tutor : UserRole.Parent;

        try
        {
            var result = await classSessionService.CancelClassSessionAsync(id, userId, role, reason);
            return Ok(MV.DomainLayer.DTO.APIResponse<ClassSessionResponse>.Success(result, "Buổi học đã được hủy thành công."));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(MV.DomainLayer.DTO.APIResponse<object>.Fail(ex.Message, 404));
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, MV.DomainLayer.DTO.APIResponse<object>.Fail(ex.Message, 403));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(MV.DomainLayer.DTO.APIResponse<object>.Fail(ex.Message, 400));
        }
    }

    /// <summary>
    /// POST /api/class-sessions/{id}/report-no-show
    /// Parent báo cáo gia sư vắng mặt sau 15 phút kể từ giờ bắt đầu.
    /// </summary>
    [HttpPost("class-sessions/{id:int}/report-no-show")]
    [Authorize(Roles = UserRole.ParentOrStudent)]
    public async Task<IActionResult> ReportNoShow([FromRoute] int id, [FromBody] ReportNoShowRequest? request)
    {
        var userId = UserId ?? throw new UnauthorizedAccessException();
        var role = User.FindFirstValue(ClaimTypes.Role) ?? "";
        try
        {
            var result = await classSessionService.ReportTutorNoShowAsync(id, userId, role, request);
            return Ok(MV.DomainLayer.DTO.APIResponse<ClassSessionDetailResponse>.Success(result, "Đã báo cáo gia sư vắng mặt thành công."));
        }
        catch (ClassSessionException ex)
        {
            return StatusCode(ex.HttpStatus, MV.DomainLayer.DTO.APIResponse<object>.Fail(ex.Message, ex.HttpStatus));
        }
    }

    /// <summary>
    /// POST /api/class-sessions/{id}/no-show-action
    /// Parent (hoặc học sinh tự quản) chọn hướng xử lý sau khi gia sư bị xác nhận vắng mặt.
    /// ActionType: free_session | makeup | change_tutor
    /// </summary>
    [HttpPost("class-sessions/{id:int}/no-show-action")]
    [Authorize(Roles = UserRole.ParentOrStudent)]
    public async Task<IActionResult> ProcessNoShowAction([FromRoute] int id, [FromBody] NoShowActionRequest request)
    {
        var userId = UserId ?? throw new UnauthorizedAccessException();
        var role = User.FindFirstValue(ClaimTypes.Role) ?? "";
        try
        {
            var result = await classSessionService.ProcessNoShowActionAsync(id, userId, role, request);
            return Ok(MV.DomainLayer.DTO.APIResponse<NoShowActionResultResponse>.Success(result, "Xử lý no-show thành công."));
        }
        catch (ClassSessionException ex)
        {
            return StatusCode(ex.HttpStatus, MV.DomainLayer.DTO.APIResponse<object>.Fail(ex.Message, ex.HttpStatus));
        }
    }

    [HttpPost("debug/bookings/{id}/create-class-sessions")]
    [Authorize(Roles = UserRole.Admin)]
    public async Task<IActionResult> DebugCreateClassSessions([FromRoute] int id)
    {
        await classSessionService.AutoCreateClassSessionsAsync(id);
        return Ok(new { message = $"ClassSessions created for booking {id}" });
    }
}
