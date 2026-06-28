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
public class LessonController(ILessonService lessonService) : ControllerBase
{
    private string? UserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    // Moved to TutorLessonController → GET api/tutor/lessons
    // [HttpGet("tutor/lessons")]

    [HttpGet("parent/lessons")]
    [Authorize(Roles = UserRole.Parent)]
    public async Task<IActionResult> GetParentLessons(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] string? status = null)
    {
        var parentId = UserId ?? throw new UnauthorizedAccessException();
        var result = await lessonService.GetParentLessonsAsync(parentId, page, pageSize, fromDate, status);
        return Ok(MV.DomainLayer.DTO.APIResponse<PagedList<LessonResponse>>.Success(result, ApiMessages.Success));
    }

    [HttpGet("lessons/{id}")]
    [Authorize]
    public async Task<IActionResult> GetLessonById([FromRoute] int id)
    {
        var userId = UserId ?? string.Empty;
        var isParent = User.IsInRole(UserRole.Parent);
        var result = await lessonService.GetLessonByIdAsync(id, userId, isParent);

        if (result == null)
            return NotFound(new { message = ApiMessages.LessonNotFound });

        return Ok(MV.DomainLayer.DTO.APIResponse<LessonResponse>.Success(result, ApiMessages.Success));
    }

    /// <summary>
    /// DELETE /api/lessons/{id}?reason=...
    /// KHÔNG hỗ trợ hủy từng buổi học lẻ (luôn trả 400) — việc giải phóng escrow đòi hỏi
    /// hủy toàn bộ booking. Endpoint giữ lại để client nhận thông báo hướng dẫn thay vì 404.
    /// </summary>
    [HttpDelete("lessons/{id:int}")]
    [Authorize(Roles = UserRole.ParentOrTutor)]
    public async Task<IActionResult> CancelLesson([FromRoute] int id, [FromQuery] string? reason = null)
    {
        var userId = UserId ?? throw new UnauthorizedAccessException();
        var role = User.IsInRole(UserRole.Tutor) ? UserRole.Tutor : UserRole.Parent;

        try
        {
            var result = await lessonService.CancelLessonAsync(id, userId, role, reason);
            return Ok(MV.DomainLayer.DTO.APIResponse<LessonResponse>.Success(result, "Buổi học đã được hủy thành công."));
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
    /// POST /api/lessons/{id}/report-no-show
    /// Parent báo cáo gia sư vắng mặt sau 15 phút kể từ giờ bắt đầu.
    /// </summary>
    [HttpPost("lessons/{id:int}/report-no-show")]
    [Authorize(Roles = UserRole.Parent)]
    public async Task<IActionResult> ReportNoShow([FromRoute] int id)
    {
        var parentId = UserId ?? throw new UnauthorizedAccessException();
        try
        {
            var result = await lessonService.ReportTutorNoShowAsync(id, parentId);
            return Ok(MV.DomainLayer.DTO.APIResponse<LessonDetailResponse>.Success(result, "Đã báo cáo gia sư vắng mặt thành công."));
        }
        catch (LessonException ex)
        {
            return StatusCode(ex.HttpStatus, MV.DomainLayer.DTO.APIResponse<object>.Fail(ex.Message, ex.HttpStatus));
        }
    }

    /// <summary>
    /// POST /api/lessons/{id}/no-show-action
    /// Parent chọn hướng xử lý sau khi gia sư bị xác nhận vắng mặt.
    /// ActionType: free_session | makeup | change_tutor
    /// </summary>
    [HttpPost("lessons/{id:int}/no-show-action")]
    [Authorize(Roles = UserRole.Parent)]
    public async Task<IActionResult> ProcessNoShowAction([FromRoute] int id, [FromBody] NoShowActionRequest request)
    {
        var parentId = UserId ?? throw new UnauthorizedAccessException();
        try
        {
            var result = await lessonService.ProcessNoShowActionAsync(id, parentId, request);
            return Ok(MV.DomainLayer.DTO.APIResponse<NoShowActionResultResponse>.Success(result, "Xử lý no-show thành công."));
        }
        catch (LessonException ex)
        {
            return StatusCode(ex.HttpStatus, MV.DomainLayer.DTO.APIResponse<object>.Fail(ex.Message, ex.HttpStatus));
        }
    }

    [HttpPost("debug/bookings/{id}/create-lessons")]
    [Authorize(Roles = UserRole.Admin)]
    public async Task<IActionResult> DebugCreateLessons([FromRoute] int id)
    {
        await lessonService.AutoCreateLessonsAsync(id);
        return Ok(new { message = $"Lessons created for booking {id}" });
    }
}
