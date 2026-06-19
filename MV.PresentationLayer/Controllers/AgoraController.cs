using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MV.ApplicationLayer.Interfaces;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO;
using System.Security.Claims;

namespace MV.PresentationLayer.Controllers;

/// <summary>
/// Agora RTC Controller — cung cấp Token và ChannelName để client join video call.
///
/// Flow hoạt động:
///   1. Tutor check-in → backend gán Meetinglink = lessonId (ChannelName)
///   2. Client (Tutor/Student/Parent) gọi GET /api/agora/room/{lessonId} → nhận Token + ChannelName + AppId
///   3. Client dùng Agora SDK để join channel bằng Token + ChannelName + AppId
/// </summary>
[ApiController]
[Route("api/agora")]
[Authorize]
public class AgoraController(
    IAgoraService agoraService,
    IAppDbContext context) : ControllerBase
{
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new UnauthorizedAccessException("Không tìm thấy UserId trong token.");
    private string? CurrentUserRole => User.FindFirstValue("http://schemas.microsoft.com/ws/2008/06/identity/claims/role");

    /// <summary>
    /// GET /api/agora/room/{lessonId}
    /// Lấy thông tin để join Agora channel của một buổi học.
    /// Chỉ Tutor, Parent hoặc Student thuộc buổi học mới có thể gọi endpoint này.
    /// </summary>
    /// <param name="lessonId">ID của buổi học</param>
    /// <returns>{ channelName, userId, token, appId, expireAt }</returns>
    [HttpGet("room/{lessonId:int}")]
    public async Task<IActionResult> GetRoomInfo(int lessonId)
    {
        var userId = UserId;

        // Kiểm tra buổi học tồn tại và quyền truy cập
        var lesson = await context.Lessons
            .Include(l => l.Tutor)
                .ThenInclude(t => t.Tutor)
            .Include(l => l.Booking)
                .ThenInclude(b => b!.Parent)
            .Include(l => l.Booking)
                .ThenInclude(b => b!.Student)
            .FirstOrDefaultAsync(l => l.Lessonid == lessonId);

        if (lesson == null)
            return NotFound(APIResponse<object>.Fail("Không tìm thấy buổi học.", 404));

        var hasAccess = await CheckLessonAccessAsync(lesson, userId);
        if (!hasAccess)
            return Forbid();

        // Buổi học phải sắp diễn ra hoặc đang diễn ra (không quá 30 phút trước giờ bắt đầu)
        var now          = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
        var allowedFrom  = lesson.Scheduledstart.AddMinutes(-30);
        if (now < allowedFrom)
        {
            var minutesLeft = (int)(allowedFrom - now).TotalMinutes;
            return BadRequest(APIResponse<object>.Fail(
                $"Phòng học chưa mở. Vui lòng thử lại sau {minutesLeft} phút.", 400));
        }

        // Buổi học đã kết thúc quá lâu
        var endDeadline = lesson.Scheduledend.AddHours(4);
        if (now > endDeadline)
            return BadRequest(APIResponse<object>.Fail("Buổi học đã kết thúc.", 400));

        var roomInfo = agoraService.GetRoomInfo(lessonId, userId);

        // Tên thật của các người tham gia
        var participantNames = new Dictionary<string, string>();
        if (lesson.Tutor?.Tutor != null)
            participantNames[lesson.Tutorid] = lesson.Tutor.Tutor.Fullname ?? "Gia sư";
        if (lesson.Booking?.Parent != null)
            participantNames[lesson.Booking.Parentid] = lesson.Booking.Parent.Fullname ?? "Phụ huynh";
        if (lesson.Booking?.Student != null && !string.IsNullOrEmpty(lesson.Booking.Student.Linkeduserid))
            participantNames[lesson.Booking.Student.Linkeduserid] = lesson.Booking.Student.Fullname ?? "Học sinh";

        return Ok(APIResponse<object>.Success(new
        {
            channelName      = roomInfo.ChannelName,
            userId           = roomInfo.UserId,
            token            = roomInfo.Token,
            appId            = roomInfo.AppId,
            expireAt         = roomInfo.ExpireAt,
            participantNames = participantNames
        }, "Lấy thông tin phòng Agora thành công."));
    }

    /// <summary>
    /// GET /api/agora/token?channel={channelName}
    /// Tạo Agora token nhanh cho user hiện tại (không gắn với buổi học cụ thể).
    /// Dùng cho testing.
    /// </summary>
    [HttpGet("token")]
    public IActionResult GetToken([FromQuery] string channel = "test")
    {
        var userId  = UserId;
        var token   = agoraService.GenerateToken(channel, userId);
        var expireAt = (int)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 86400);

        return Ok(APIResponse<object>.Success(new
        {
            channelName = channel,
            userId,
            token,
            expireAt
        }, "Tạo Agora token thành công."));
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task<bool> CheckLessonAccessAsync(MV.DomainLayer.Entities.Lesson lesson, string userId)
    {
        if (CurrentUserRole == MV.DomainLayer.Constants.UserRole.Admin)
            return true;

        if (lesson.Tutorid == userId)
            return true;

        if (lesson.Booking?.Parentid == userId)
            return true;

        var studentId = lesson.Booking?.Studentid;
        if (!string.IsNullOrEmpty(studentId))
        {
            var linkedUserId = await context.Studentprofiles
                .Where(s => s.Studentid == studentId)
                .Select(s => s.Linkeduserid)
                .FirstOrDefaultAsync();

            if (linkedUserId == userId)
                return true;
        }

        return false;
    }
}
