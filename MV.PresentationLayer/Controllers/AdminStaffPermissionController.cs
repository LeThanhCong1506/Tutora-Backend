using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO;
using MV.DomainLayer.DTO.ResponseModel;

namespace MV.PresentationLayer.Controllers;

/// <summary>Compatibility reads for the former direct Staff-permission API.</summary>
[ApiController]
[Route("api/admin/staff-permissions")]
[Authorize(Roles = UserRole.Admin)]
public class AdminStaffPermissionController : ControllerBase
{
    private readonly IPermissionGroupService _service;

    public AdminStaffPermissionController(IPermissionGroupService service)
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

    [HttpGet("{staffId}")]
    public async Task<IActionResult> GetStaffPermissions(string staffId)
    {
        try
        {
            var assignment = await _service.GetStaffAssignmentAsync(staffId);
            var result = new StaffPermissionsResponse
            {
                StaffId = assignment.StaffId,
                StaffFullName = assignment.StaffFullName,
                PermissionKeys = assignment.PermissionKeys
            };
            return Ok(APIResponse<StaffPermissionsResponse>.Success(
                result, "Lấy quyền hiệu lực của Staff thành công."));
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
}
