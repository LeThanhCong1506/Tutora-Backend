using MV.DomainLayer.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.DTO;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Exceptions;
using System.Security.Claims;

namespace MV.PresentationLayer.Controllers;

/// <summary>
/// Admin only: quản lý permission cấp cho từng Staff. Chính bản thân việc cấp
/// quyền không bao giờ được delegate — nếu Staff cấp được quyền thì có thể
/// tự cấp cho mình mọi thứ, sập toàn bộ mô hình phân quyền.
/// </summary>
[ApiController]
[Route("api/admin/staff-permissions")]
[Authorize(Roles = UserRole.Admin)]
public class AdminStaffPermissionController : ControllerBase
{
    private readonly IUserService _userService;

    public AdminStaffPermissionController(IUserService userService)
    {
        _userService = userService;
    }

    [HttpGet("catalog")]
    public IActionResult GetCatalog()
    {
        var catalog = Permissions.Catalog
            .Select(p => new PermissionCatalogItemResponse { Key = p.Key, Label = p.Label, Group = p.Group })
            .ToList();

        return Ok(APIResponse<List<PermissionCatalogItemResponse>>.Success(catalog, "Lấy danh mục quyền thành công."));
    }

    [HttpGet("{staffId}")]
    public async Task<IActionResult> GetStaffPermissions(string staffId)
    {
        try
        {
            var result = await _userService.AdminGetStaffPermissionsAsync(staffId);
            return Ok(APIResponse<StaffPermissionsResponse>.Success(result, "Lấy danh sách quyền của Staff thành công."));
        }
        catch (UserNotFoundException ex)
        {
            return NotFound(APIResponse<object>.Fail(ex.Message, 404));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(APIResponse<object>.Fail(ex.Message, 400));
        }
    }

    [HttpPut("{staffId}")]
    public async Task<IActionResult> SetStaffPermissions(string staffId, [FromBody] SetStaffPermissionsRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(APIResponse<object>.Fail(ApiMessages.InvalidInputData, 400));

        var adminId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(adminId))
            return Unauthorized(APIResponse<object>.Fail(ApiMessages.Unauthorized, 401));

        try
        {
            var result = await _userService.AdminSetStaffPermissionsAsync(staffId, request.PermissionKeys, adminId);
            return Ok(APIResponse<StaffPermissionsResponse>.Success(result, "Cập nhật quyền cho Staff thành công."));
        }
        catch (UserNotFoundException ex)
        {
            return NotFound(APIResponse<object>.Fail(ex.Message, 404));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(APIResponse<object>.Fail(ex.Message, 400));
        }
    }
}
