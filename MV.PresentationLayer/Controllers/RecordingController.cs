using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO;

namespace MV.PresentationLayer.Controllers;

/// <summary>
/// Thao tác thủ công Agora Cloud Recording — dùng để TEST độc lập (Admin).
///
/// Luồng thật đã chạy TỰ ĐỘNG: auto check-in (cả gia sư + học viên cùng vào phòng học
/// chính) → start; check-out → stop (xem ClassSessionService.TryAutoCheckInAsync /
/// CheckOutAsync). Controller này chỉ để kiểm thử API mà không cần đi qua toàn bộ luồng buổi học.
/// </summary>
[ApiController]
[Route("api/recording")]
[Authorize(Roles = UserRole.Admin)]
public class RecordingController(ICloudRecordingService recording) : ControllerBase
{
    /// <summary>
    /// POST /api/recording/{classSessionId}/start
    /// Bắt đầu record. Trả về resourceId + sid — GIỮ LẠI để gọi stop.
    /// </summary>
    [HttpPost("{classSessionId:int}/start")]
    public async Task<IActionResult> Start(int classSessionId)
    {
        var handle = await recording.StartAsync(classSessionId);
        return Ok(APIResponse<object>.Success(
            new { handle.ResourceId, handle.Sid }, "Đã bắt đầu Cloud Recording."));
    }

    /// <summary>
    /// POST /api/recording/{classSessionId}/stop?resourceId=...&amp;sid=...
    /// Dừng record. Cần resourceId + sid từ bước start.
    /// </summary>
    [HttpPost("{classSessionId:int}/stop")]
    public async Task<IActionResult> Stop(int classSessionId, [FromQuery] string resourceId, [FromQuery] string sid)
    {
        if (string.IsNullOrWhiteSpace(resourceId) || string.IsNullOrWhiteSpace(sid))
            return BadRequest(APIResponse<object>.Fail("Thiếu resourceId hoặc sid.", 400));

        var result = await recording.StopAsync(classSessionId, resourceId, sid);
        return Ok(APIResponse<object>.Success(
            new { result.FileNames, result.PlaybackUrl }, "Đã dừng Cloud Recording."));
    }
}
