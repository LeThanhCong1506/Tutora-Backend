using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using System.Security.Claims;

namespace MV.PresentationLayer.Controllers;

[Route("api/staffs")]
[ApiController]
[Authorize(Roles = UserRole.Admin)]
public class StaffController : ControllerBase
{
    private readonly IUserService _userService;

    public StaffController(IUserService userService)
    {
        _userService = userService;
    }

    [HttpGet]
    public async Task<IActionResult> GetStaffs([FromQuery] UserParameters parameters)
    {
        var result = await _userService.GetUsersByRoleAsync(UserRole.Staff, parameters);
        return Ok(APIResponse<PagedList<UserResponse>>.Success(result, "Lấy danh sách nhân viên thành công."));
    }

    [HttpPost]
    public async Task<IActionResult> CreateStaff([FromBody] CreateStaffRequest request)
    {
        var adminUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(adminUserId))
            return Unauthorized(APIResponse.Fail(ApiMessages.Unauthorized, 401));
        try
        {
            var createdStaff = await _userService.CreateStaffAsync(request, adminUserId);
            return StatusCode(201, APIResponse<UserResponse>.Success(
                createdStaff, "Tạo tài khoản nhân viên thành công.", 201));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(APIResponse.Fail(ex.Message, 404));
        }
    }
}
