using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MV.ApplicationLayer.Interfaces;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO;
using MV.PresentationLayer.Helpers;
using System.Security.Claims;

namespace MV.PresentationLayer.Controllers;

/// <summary>
/// Tencent RTC Controller — cung cấp UserSig và RoomId để client join video call.
/// 
/// Flow hoạt động:
///   1. Tutor check-in → backend gán Meetinglink = classSessionId (RoomId)
///   2. Client (Tutor/Student/Parent) gọi GET /api/trtc/room/{classSessionId} → nhận UserSig + RoomId
///   3. Client dùng TRTC SDK để join room bằng UserSig + RoomId + SdkAppId
/// </summary>
[ApiController]
[Route("api/trtc")]
[Authorize]
public class TRTCController(
    ITencentRTCService trtcService,
    IAppDbContext context) : ControllerBase
{
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new UnauthorizedAccessException("Không tìm thấy UserId trong token.");
    private string? CurrentUserRole => User.FindFirstValue("http://schemas.microsoft.com/ws/2008/06/identity/claims/role");

    /// <summary>
    /// GET /api/trtc/room/{classSessionId}
    /// Lấy thông tin để join phòng Tencent RTC của một buổi học.
    /// Chỉ Tutor, Parent hoặc Student thuộc buổi học mới có thể gọi endpoint này.
    /// </summary>
    /// <param name="classSessionId">ID của buổi học</param>
    /// <returns>{ roomId, userId, userSig, sdkAppId, expireAt }</returns>
    [HttpGet("room/{classSessionId:int}")]
    public async Task<IActionResult> GetRoomInfo(int classSessionId)
    {
        var userId = UserId;

        // Kiểm tra buổi học tồn tại và quyền truy cập
        var classSession = await context.ClassSessions
            .Include(l => l.Tutor)
                .ThenInclude(t => t.Tutor)
            .Include(l => l.Booking)
                .ThenInclude(b => b!.Parent)
            .Include(l => l.Booking)
                .ThenInclude(b => b!.Student)
            .FirstOrDefaultAsync(l => l.Classsessionid == classSessionId);

        if (classSession == null)
            return NotFound(APIResponse<object>.Fail("Không tìm thấy buổi học.", 404));

        // Kiểm tra quyền: chỉ Tutor, Parent của booking, hoặc Student được join
        var hasAccess = await CheckClassSessionAccessAsync(classSession, userId);
        if (!hasAccess)
            return Forbid();

        // Buổi học phải đang diễn ra hoặc sắp diễn ra (không quá 30 phút trước giờ bắt đầu)
        var now = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
        var allowedFrom = classSession.Scheduledstart.AddMinutes(-30);
        if (now < allowedFrom)
        {
            var minutesLeft = (int)(allowedFrom - now).TotalMinutes;
            return BadRequest(APIResponse<object>.Fail(
                $"Phòng học chưa mở. Vui lòng thử lại sau {minutesLeft} phút.", 400));
        }

        // Buổi học đã kết thúc quá lâu (sau checkout hoặc 4 tiếng từ khi bắt đầu)
        var endDeadline = classSession.Scheduledend.AddHours(4);
        if (now > endDeadline)
        {
            return BadRequest(APIResponse<object>.Fail("Buổi học đã kết thúc.", 400));
        }

        // Lấy thông tin phòng
        var roomInfo = trtcService.GetRoomInfo(classSessionId, userId);

        // Lấy tên thật của người tham gia
        var participantNames = new Dictionary<string, string>();
        if (classSession.Tutor?.Tutor != null) {
            participantNames[classSession.Tutorid] = classSession.Tutor.Tutor.Fullname ?? "Gia sư";
        }
        if (classSession.Booking?.Parent != null) {
            participantNames[classSession.Booking.Parentid] = classSession.Booking.Parent.Fullname ?? "Phụ huynh";
        }
        if (classSession.Booking?.Student != null) {
            if (!string.IsNullOrEmpty(classSession.Booking.Student.Linkeduserid)) {
                participantNames[classSession.Booking.Student.Linkeduserid] = classSession.Booking.Student.Fullname ?? "Học sinh";
            }
        }

        return Ok(APIResponse<object>.Success(new
        {
            roomId     = roomInfo.RoomId,
            userId     = roomInfo.UserId,
            userSig    = roomInfo.UserSig,
            sdkAppId   = roomInfo.SdkAppId,
            expireAt   = roomInfo.ExpireAt,
            participantNames = participantNames
        }, "Lấy thông tin phòng Tencent RTC thành công."));
    }

    /// <summary>
    /// GET /api/trtc/token
    /// Tạo UserSig nhanh cho user hiện tại (không gắn với buổi học cụ thể).
    /// Dùng cho testing hoặc khi cần token độc lập.
    /// </summary>
    [HttpGet("token")]
    public IActionResult GetUserSig()
    {
        var userId = UserId;
        var userSig = trtcService.GenerateUserSig(userId);
        var expireAt = (int)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 86400);

        return Ok(APIResponse<object>.Success(new
        {
            userId,
            userSig,
            expireAt
        }, "Tạo UserSig thành công."));
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task<bool> CheckClassSessionAccessAsync(MV.DomainLayer.Entities.ClassSession classSession, string userId)
    {
        // Admin có toàn quyền
        if (CurrentUserRole == MV.DomainLayer.Constants.UserRole.Admin)
            return true;

        // Tutor của buổi học
        if (classSession.Tutorid == userId)
            return true;

        // Parent của booking
        if (classSession.Booking?.Parentid == userId)
            return true;

        // Student có tài khoản riêng (linkedUserId)
        var studentId = classSession.Booking?.Studentid;
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
