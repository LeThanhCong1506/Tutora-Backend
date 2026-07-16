using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.DTO;
using MV.DomainLayer.DTO.ResponseModel;
using System.Security.Claims;

namespace MV.PresentationLayer.Controllers;

[ApiController]
[Route("api/access")]
[Authorize(Roles = MV.DomainLayer.Constants.UserRole.AdminOrStaff)]
public sealed class AccessController : ControllerBase
{
    private readonly IPermissionGroupService _service;

    public AccessController(IPermissionGroupService service)
    {
        _service = service;
    }

    [HttpGet("me")]
    public async Task<IActionResult> GetMe()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return Unauthorized(APIResponse.Fail("Không xác định được người dùng.", 401));
        try
        {
            var result = await _service.GetAccessAsync(userId);
            return Ok(APIResponse<AccessMeResponse>.Success(result, "Lấy quyền truy cập thành công."));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(APIResponse.Fail(ex.Message, 404));
        }
    }
}
