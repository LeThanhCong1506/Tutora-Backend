using MV.DomainLayer.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.DTO;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Exceptions;
using MV.PresentationLayer.Authorization;
using System.Security.Claims;
using System.Text.Json;

namespace MV.PresentationLayer.Controllers;

[ApiController]
[Route("api/admin/users")]
[Authorize]
public class AdminUserController : ControllerBase
{
    private readonly IUserService _userService;

    public AdminUserController(IUserService userService)
    {
        _userService = userService;
    }

    [RequirePermission(Permissions.UserView)]
    [HttpGet]
    public async Task<IActionResult> GetAllUsers([FromQuery] AdminUserFilterParameters parameters)
    {
        var users = await _userService.AdminGetAllUsersAsync(
            parameters, includeInternalAccounts: User.IsInRole(UserRole.Admin));

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

    /// <summary>
    /// POST /api/admin/users
    /// Tạo tài khoản khách hàng mới (Student / Parent / Tutor). Tài khoản nội bộ
    /// (Staff/Admin) không tạo qua đây — dùng POST /api/staffs.
    /// </summary>
    [RequirePermission(Permissions.UserUpdate)]
    [HttpPost]
    public async Task<IActionResult> CreateUser([FromBody] AdminCreateUserRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(APIResponse<object>.Fail(ApiMessages.InvalidInputData, 400));

        var adminId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(adminId))
            return Unauthorized(APIResponse<object>.Fail(ApiMessages.Unauthorized, 401));

        try
        {
            var created = await _userService.AdminCreateUserAsync(request, adminId);
            return StatusCode(201, APIResponse<UserResponse>.Success(
                created, "Tạo tài khoản người dùng thành công.", 201));
        }
        catch (InvalidOperationException ex)
        {
            // Preserve the specific validation message (invalid role) instead of
            // the generic one the global handler would substitute.
            return BadRequest(APIResponse<object>.Fail(ex.Message, 400));
        }
        catch (EmailAlreadyExistsException ex)
        {
            return BadRequest(APIResponse<object>.Fail(ex.Message, 400));
        }
        catch (PhoneAlreadyExistsException ex)
        {
            return BadRequest(APIResponse<object>.Fail(ex.Message, 400));
        }
    }

    [RequirePermission(Permissions.UserView)]
    [HttpGet("{id}")]
    public async Task<IActionResult> GetUserById(string id)
    {
        try
        {
            var guardResult = await GuardInternalTargetAsync(id, mutation: false);
            if (guardResult != null) return guardResult;

            var user = await _userService.GetUserByIdAsync(id);
            return Ok(APIResponse<UserResponse>.Success(user, "Lấy thông tin người dùng thành công."));
        }
        catch (UserNotFoundException ex)
        {
            return NotFound(APIResponse<object>.Fail(ex.Message, 404));
        }
    }

    [RequirePermission(Permissions.UserUpdate)]
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateUser(string id, [FromBody] AdminUpdateUserRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(APIResponse<object>.Fail(ApiMessages.InvalidInputData, 400));

        try
        {
            var guardResult = await GuardInternalTargetAsync(id, mutation: true);
            if (guardResult != null) return guardResult;

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

    [RequirePermission(Permissions.UserDeactivate)]
    [HttpPut("{id}/deactivate")]
    public async Task<IActionResult> DeactivateUser(string id)
    {
        try
        {
            var guardResult = await GuardInternalTargetAsync(id, mutation: true);
            if (guardResult != null) return guardResult;

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
    [RequirePermission(Permissions.UserDeactivate)]
    [HttpPut("{id}/reactivate")]
    public async Task<IActionResult> ReactivateUser(string id)
    {
        try
        {
            var guardResult = await GuardInternalTargetAsync(id, mutation: true);
            if (guardResult != null) return guardResult;

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

    [Authorize(Roles = UserRole.Admin)]
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
    [Authorize(Roles = UserRole.Admin)]
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
    private async Task<IActionResult?> GuardInternalTargetAsync(string targetUserId, bool mutation)
    {
        var target = await _userService.GetUserByIdAsync(targetUserId);
        var isAdminTarget = string.Equals(target.Role, UserRole.Admin, StringComparison.OrdinalIgnoreCase);
        var isStaffTarget = string.Equals(target.Role, UserRole.Staff, StringComparison.OrdinalIgnoreCase);

        // Delegated user permissions are only for customer accounts.
        if (!User.IsInRole(UserRole.Admin) && (isAdminTarget || isStaffTarget))
            return StatusCode(403, APIResponse<object>.Fail(
                "Staff không được xem hoặc thao tác tài khoản nội bộ.", 403));

        // Even Admin must use dedicated role/staff-management flows for internal accounts.
        if (mutation && isAdminTarget)
            return StatusCode(403, APIResponse<object>.Fail(
                "Không thể sửa hoặc khóa tài khoản Admin qua endpoint được phân quyền.", 403));

        return null;
    }
}
