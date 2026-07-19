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
[Route("api/admin/permission-groups")]
[Authorize(Roles = UserRole.Admin)]
public sealed class AdminPermissionGroupController : ControllerBase
{
    private readonly IPermissionGroupService _service;

    public AdminPermissionGroupController(IPermissionGroupService service)
    {
        _service = service;
    }

    [HttpGet("catalog")]
    public IActionResult GetCatalog()
    {
        var catalog = Permissions.Catalog.Select(p => new PermissionCatalogItemResponse
        {
            Key = p.Key,
            Label = p.Label,
            Domain = p.Domain,
            Module = p.Module,
            Action = p.Action,
            Description = p.Label,
            Requires = p.Requires
        }).ToList();
        return Ok(APIResponse<List<PermissionCatalogItemResponse>>.Success(
            catalog, "Lấy danh mục quyền thành công."));
    }

    [HttpGet]
    public async Task<IActionResult> GetGroups([FromQuery] PermissionGroupListParameters parameters)
    {
        var result = await _service.GetGroupsAsync(parameters);
        return Ok(APIResponse<PagedList<PermissionGroupSummaryResponse>>.Success(
            result, "Lấy danh sách nhóm quyền thành công."));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetGroup(Guid id)
    {
        try
        {
            var result = await _service.GetGroupAsync(id);
            return Ok(APIResponse<PermissionGroupDetailResponse>.Success(
                result, "Lấy nhóm quyền thành công."));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(APIResponse.Fail(ex.Message, 404));
        }
    }

    [HttpPost]
    public async Task<IActionResult> CreateGroup([FromBody] CreatePermissionGroupRequest request)
    {
        var actor = GetActorUserId();
        if (actor == null)
            return Unauthorized(APIResponse.Fail(ApiMessages.Unauthorized, 401));
        try
        {
            var result = await _service.CreateGroupAsync(request, actor);
            return StatusCode(201, APIResponse<PermissionGroupDetailResponse>.Success(
                result, "Tạo nhóm quyền thành công.", 201));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(APIResponse.Fail(ex.Message, 400));
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(APIResponse.Fail(ex.Message, 409));
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateGroup(Guid id, [FromBody] UpdatePermissionGroupRequest request)
    {
        var actor = GetActorUserId();
        if (actor == null)
            return Unauthorized(APIResponse.Fail(ApiMessages.Unauthorized, 401));
        try
        {
            var result = await _service.UpdateGroupAsync(id, request, actor);
            return Ok(APIResponse<PermissionGroupDetailResponse>.Success(
                result, "Cập nhật nhóm quyền thành công."));
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
            return Conflict(APIResponse.Fail(ex.Message, 409));
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteGroup(Guid id, [FromQuery] long? expectedVersion)
    {
        if (!expectedVersion.HasValue || expectedVersion < 0)
            return BadRequest(APIResponse.Fail("expectedVersion là bắt buộc.", 400));
        var actor = GetActorUserId();
        if (actor == null)
            return Unauthorized(APIResponse.Fail(ApiMessages.Unauthorized, 401));
        try
        {
            await _service.DeleteGroupAsync(id, expectedVersion.Value, actor);
            return Ok(APIResponse.Success("Xóa nhóm quyền thành công."));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(APIResponse.Fail(ex.Message, 404));
        }
        catch (PermissionVersionConflictException ex)
        {
            return Conflict(APIResponse.Fail(ex.Message, 409, new { ex.CurrentVersion }));
        }
        catch (PermissionGroupInUseException ex)
        {
            return Conflict(APIResponse.Fail(ex.Message, 409, new { ex.AssignedStaffCount }));
        }
    }

    private string? GetActorUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier);
}
