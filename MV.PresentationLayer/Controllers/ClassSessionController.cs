using MV.DomainLayer.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
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
    /// DELETE /api/classSessions/{id}?reason=...
    /// Tutor hoặc Parent có thể hủy 1 buổi học đang được lên lịch (status = scheduled).
    /// Không ảnh hưởng các buổi học khác trong cùng booking.
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

    [HttpPost("debug/bookings/{id}/create-class-sessions")]
    [Authorize(Roles = UserRole.Admin)]
    public async Task<IActionResult> DebugCreateClassSessions([FromRoute] int id)
    {
        await classSessionService.AutoCreateClassSessionsAsync(id);
        return Ok(new { message = $"ClassSessions created for booking {id}" });
    }
}
