using MV.DomainLayer.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.DTO;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;

namespace MV.PresentationLayer.Controllers
{
    /// <summary>
    /// Quản lý tài khoản nhân viên nội bộ (Staff) — tách khỏi UserController
    /// để endpoint tập trung đúng một domain. Toàn bộ thao tác do Admin thực
    /// hiện; tài khoản khách hàng (Tutor/Parent/Student) đăng ký qua auth flows.
    /// </summary>
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
            var createdStaff = await _userService.CreateStaffAsync(request);

            var response = APIResponse<UserResponse>.Success(createdStaff, "Tạo tài khoản nhân viên thành công.", 201);

            return StatusCode(201, response);
        }
    }
}
