using MV.DomainLayer.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.DTO;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Exceptions;
using System.Security.Claims;
using System.Text.Json;

namespace MV.PresentationLayer.Controllers;

[ApiController]
[Route("api/admin/users")]
[Authorize(Roles = UserRole.Admin)]
public class AdminUserController : ControllerBase
{
    private readonly IUserService _userService;

    public AdminUserController(IUserService userService)
    {
        _userService = userService;
    }

    [HttpGet]
    public async Task<IActionResult> GetAllUsers([FromQuery] AdminUserFilterParameters parameters)
    {
        var users = await _userService.AdminGetAllUsersAsync(parameters);

        var metadata = new
        {
            users.TotalCount,
            users.PageSize,
            users.CurrentPage,
            users.TotalPages,
            users.HasNext,
            users.HasPrevious
        };

        Response.Headers.Add("X-Pagination", JsonSerializer.Serialize(metadata));

        return Ok(APIResponse<PagedList<UserResponse>>.Success(users, "Lấy danh sách người dùng thành công."));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetUserById(string id)
    {
        try
        {
            var user = await _userService.GetUserByIdAsync(id);
            return Ok(APIResponse<UserResponse>.Success(user, "Lấy thông tin người dùng thành công."));
        }
        catch (UserNotFoundException ex)
        {
            return NotFound(APIResponse<object>.Fail(ex.Message, 404));
        }
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateUser(string id, [FromBody] AdminUpdateUserRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(APIResponse<object>.Fail(ApiMessages.InvalidInputData, 400));

        try
        {
            await _userService.AdminUpdateUserAsync(id, request);
            return Ok(APIResponse<object>.Success(null!, "Cập nhật thông tin người dùng thành công."));
        }
        catch (UserNotFoundException ex)
        {
            return NotFound(APIResponse<object>.Fail(ex.Message, 404));
        }
        catch (Exception ex)
        {
            return StatusCode(500, APIResponse<object>.Fail(ApiMessages.GenericErrorPrefix + ex.Message, 500));
        }
    }

    [HttpPut("{id}/deactivate")]
    public async Task<IActionResult> DeactivateUser(string id)
    {
        try
        {
            await _userService.AdminDeactivateUserAsync(id);
            return Ok(APIResponse<object>.Success(null!, "Vô hiệu hóa tài khoản người dùng thành công."));
        }
        catch (UserNotFoundException ex)
        {
            return NotFound(APIResponse<object>.Fail(ex.Message, 404));
        }
        catch (Exception ex)
        {
            return StatusCode(500, APIResponse<object>.Fail(ApiMessages.GenericErrorPrefix + ex.Message, 500));
        }
    }

    /// <summary>
    /// PUT /api/admin/users/{id}/reactivate
    /// Mở khóa tài khoản người dùng đã bị deactivate.
    /// Nếu là gia sư và profile đang Active → khôi phục Ispublic = true.
    /// </summary>
    [HttpPut("{id}/reactivate")]
    public async Task<IActionResult> ReactivateUser(string id)
    {
        try
        {
            await _userService.AdminReactivateUserAsync(id);
            return Ok(APIResponse<object>.Success(null!, "Mở khóa tài khoản người dùng thành công."));
        }
        catch (UserNotFoundException ex)
        {
            return NotFound(APIResponse<object>.Fail(ex.Message, 404));
        }
        catch (Exception ex)
        {
            return StatusCode(500, APIResponse<object>.Fail(ApiMessages.GenericErrorPrefix + ex.Message, 500));
        }
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteUser(string id)
    {
        try
        {
            await _userService.DeleteUserAsync(id);
            return Ok(APIResponse<object>.Success(null!, "Xóa người dùng thành công."));
        }
        catch (UserNotFoundException ex)
        {
            return NotFound(APIResponse<object>.Fail(ex.Message, 404));
        }
        catch (Exception ex)
        {
            return StatusCode(500, APIResponse<object>.Fail(ApiMessages.GenericErrorPrefix + ex.Message, 500));
        }
    }

    /// <summary>
    /// PUT /api/admin/users/{id}/role
    /// Admin thay đổi role của một user.
    /// Các role được phép gán: Parent, Student, Tutor, Staff.
    /// Không thể gán role Admin qua API.
    /// </summary>
    [HttpPut("{id}/role")]
    public async Task<IActionResult> ChangeUserRole(string id, [FromBody] ChangeUserRoleRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(APIResponse<object>.Fail(ApiMessages.InvalidInputData, 400));

        var adminId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(adminId))
            return Unauthorized(APIResponse<object>.Fail(ApiMessages.Unauthorized, 401));

        try
        {
            var result = await _userService.AdminChangeUserRoleAsync(id, request.NewRole, adminId);
            return Ok(APIResponse<ChangeUserRoleResponse>.Success(
                result,
                $"Thay đổi role thành công: {result.PreviousRole} → {result.NewRole}."));
        }
        catch (UserNotFoundException ex)
        {
            return NotFound(APIResponse<object>.Fail(ex.Message, 404));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(APIResponse<object>.Fail(ex.Message, 400));
        }
        catch (Exception ex)
        {
            return StatusCode(500, APIResponse<object>.Fail(ApiMessages.GenericErrorPrefix + ex.Message, 500));
        }
    }
}
