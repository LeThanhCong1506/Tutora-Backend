using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Exceptions;
using System.Security.Claims;

namespace MV.PresentationLayer.Controllers;

[ApiController]
[Route("api/admin/staffs")]
[Authorize(Roles = UserRole.Admin)]
public sealed class AdminStaffPermissionGroupController : ControllerBase
{
    private readonly IPermissionGroupService _service;

    public AdminStaffPermissionGroupController(IPermissionGroupService service)
    {
        _service = service;
    }

    [HttpGet("{staffId}/permission-group")]
    public async Task<IActionResult> GetAssignment(string staffId)
    {
        try
        {
            var result = await _service.GetStaffAssignmentAsync(staffId);
            return Ok(APIResponse<StaffPermissionGroupResponse>.Success(
                result, "Lấy nhóm quyền của Staff thành công."));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(APIResponse.Fail(ex.Message, 404));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(APIResponse.Fail(ex.Message, 400));
        }
    }

    [HttpPut("{staffId}/permission-group")]
    public async Task<IActionResult> SetAssignment(
        string staffId, [FromBody] SetStaffPermissionGroupRequest request)
    {
        var actor = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(actor))
            return Unauthorized(APIResponse.Fail(ApiMessages.Unauthorized, 401));
        try
        {
            var result = await _service.SetStaffAssignmentAsync(staffId, request, actor);
            return Ok(APIResponse<StaffPermissionGroupResponse>.Success(
                result, "Cập nhật nhóm quyền của Staff thành công."));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(APIResponse.Fail(ex.Message, 404));
        }
        catch (PermissionVersionConflictException ex)
        {
            return Conflict(APIResponse.Fail(ex.Message, 409, new { ex.CurrentVersion }));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(APIResponse.Fail(ex.Message, 400));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(APIResponse.Fail(ex.Message, 400));
        }
    }
}
