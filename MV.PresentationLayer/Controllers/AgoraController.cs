using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MV.ApplicationLayer.Interfaces;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO;
using MV.DomainLayer.DTO.RequestModel;
using MV.PresentationLayer.Helpers;
using System.Security.Claims;

namespace MV.PresentationLayer.Controllers;

/// <summary>
/// Agora RTC Controller — cung cấp token + channel để client join video call.
///
/// Flow hoạt động:
///   1. Tutor check-in → backend gán Meetinglink = classSessionId (= channel name).
///   2. Client (Tutor/Student/Parent) gọi GET /api/agora/room/{classSessionId} → nhận token + channel + appId.
///   3. Client dùng Agora SDK join channel bằng: appId + channel + token + uid (= userId, join bằng user account).
/// </summary>
[ApiController]
[Route("api/agora")]
[Authorize]
public class AgoraController(
    IAgoraRTCService agoraService,
    IAgoraPresenceTracker presenceTracker,
    IAppDbContext context) : ControllerBase
{
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new UnauthorizedAccessException("Không tìm thấy UserId trong token.");
    private string? CurrentUserRole => User.FindFirstValue("http://schemas.microsoft.com/ws/2008/06/identity/claims/role");

    /// <summary>
    /// GET /api/agora/room/{classSessionId}
    /// Lấy thông tin để join kênh Agora RTC của một buổi học.
    /// Chỉ Tutor, Parent hoặc Student thuộc buổi học mới có thể gọi endpoint này.
    /// </summary>
    /// <param name="classSessionId">ID của buổi học</param>
    /// <returns>{ channel, uid, token, appId, expireAt, participantNames }</returns>
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

        // Không giới hạn thời gian join Agora nữa — user có thể vào phòng bất kỳ lúc nào.

        // Lấy thông tin phòng (channel + token)
        var roomInfo = agoraService.GetRoomInfo(classSessionId, userId);

        // Lấy tên thật của người tham gia (key = UserId = Agora user account)
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
            channel          = roomInfo.Channel,
            uid              = roomInfo.Uid,
            token            = roomInfo.Token,
            appId            = roomInfo.AppId,
            expireAt         = roomInfo.ExpireAt,
            participantNames = participantNames
        }, "Lấy thông tin phòng Agora RTC thành công."));
    }

    /// <summary>
    /// GET /api/agora/token?channel={channel}
    /// Tạo token nhanh cho user hiện tại trong một channel bất kỳ.
    /// Chỉ Admin gọi được — dùng cho testing nội bộ, không phục vụ người dùng cuối
    /// (channel không được kiểm tra gắn với buổi học nào, không an toàn cho user thường).
    /// </summary>
    [HttpGet("token")]
    [Authorize(Roles = UserRole.Admin)]
    public IActionResult GetToken([FromQuery] string channel)
    {
        if (string.IsNullOrWhiteSpace(channel))
            return BadRequest(APIResponse<object>.Fail("Thiếu tham số 'channel'.", 400));

        var userId = UserId;
        var token = agoraService.GenerateToken(channel, userId);

        return Ok(APIResponse<object>.Success(new
        {
            channel,
            uid = userId,
            token
        }, "Tạo Agora token thành công."));
    }

    /// <summary>
    /// POST /api/agora/{classSessionId}/presence-ping
    /// Heartbeat gửi định kỳ từ client trong lúc đang ở trong kênh Agora — dùng để tính thời gian
    /// đồng hiện diện (co-presence) giữa tutor và student. Lưu tạm in-memory, không ghi DB.
    /// </summary>
    [HttpPost("{classSessionId:int}/presence-ping")]
    public async Task<IActionResult> PresencePing(int classSessionId, [FromBody] PresencePingRequest request)
    {
        var userId = UserId;

        var classSession = await context.ClassSessions
            .Include(l => l.Booking)
            .FirstOrDefaultAsync(l => l.Classsessionid == classSessionId);

        if (classSession == null)
            return NotFound(APIResponse<object>.Fail("Không tìm thấy buổi học.", 404));

        var hasAccess = await CheckClassSessionAccessAsync(classSession, userId);
        if (!hasAccess)
            return Forbid();

        var isTutor = classSession.Tutorid == userId;

        var state = presenceTracker.RecordPing(classSessionId, isTutor, request.MicOn, request.CamOn);

        return Ok(APIResponse<object>.Success(new
        {
            videoCallStartedAt = state.VideoCallStartedAtUtc,
            accumulatedSeconds = state.AccumulatedSeconds,
            isCoPresenceConfirmed = state.IsCoPresenceConfirmed
        }, "Ghi nhận presence thành công."));
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
