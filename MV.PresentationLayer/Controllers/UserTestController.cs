using Microsoft.AspNetCore.Mvc;
using MV.ApplicationLayer.ServiceInterfaces;

namespace MV.PresentationLayer.Controllers;

[Route("api/debug")]
[ApiController]
public class UserTestController : ControllerBase
{
    private readonly IUserTestService _userTestService;

    public UserTestController(IUserTestService userTestService)
    {
        _userTestService = userTestService;
    }

    /// <summary>
    /// API xóa user cho mục đích test - XÓA TẤT CẢ DỮ LIỆU LIÊN QUAN
    /// </summary>
    [HttpDelete("users/{id}")]
    public async Task<IActionResult> DeleteUserForTest(string id)
    {
        var (success, message) = await _userTestService.DeleteUserForTestAsync(id);

        if (!success)
            return NotFound(new { success, message });

        return Ok(new { success, message });
    }

    [HttpGet("bookings/{id}/fix")]
    public async Task<IActionResult> FixBooking(int id)
    {
        var (found, message) = await _userTestService.FixBookingAsync(id);
        if (!found)
            return NotFound(message);
        return Ok(message);
    }
}
