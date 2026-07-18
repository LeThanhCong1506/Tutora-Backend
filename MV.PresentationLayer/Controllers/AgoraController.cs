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
/// Agora RTC Controller — cung cấp token + channel để client join video call.
///
/// Flow hoạt động:
///   1. Client (Tutor/Student/Parent) gọi GET /api/agora/room/{classSessionId} → nhận token +
///      channel dùng chung theo booking + appId. Phòng mở 24/7, chặn theo TRẠNG THÁI buổi học.
///   2. Client join channel bằng Agora SDK: appId + channel + token + uid (= userId).
///   3. Trong lúc ở trong phòng, client heartbeat định kỳ (POST .../heartbeat) → khi cả gia sư
///      lẫn học viên cùng có mặt, backend auto check-in buổi học.
/// </summary>
[ApiController]
[Route("api/agora")]
[Authorize]
public class AgoraController(
    IAgoraRTCService agoraService,
    IClassSessionService classSessionService,
    IWhiteboardService whiteboardService,
    ISessionPresenceService presence,
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
    /// <returns>{ channel, uid, token, appId, expireAt, tutorName, studentName, participantNames }</returns>
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

        // Chặn vào lớp buổi TIẾP THEO khi phụ huynh chưa thanh toán đợt 2 (các buổi còn
        // lại). Áp cho cả tutor lẫn học viên/phụ huynh; miễn Admin để còn giám sát.
        if (CurrentUserRole != UserRole.Admin
            && await classSessionService.IsSessionBlockedByRemainingPaymentAsync(classSessionId))
        {
            return BadRequest(APIResponse<object>.Fail(
                "Phụ huynh chưa thanh toán các buổi học còn lại. Vui lòng hoàn tất thanh toán trước khi vào lớp buổi tiếp theo.", 400));
        }

        // Phòng mở 24/7 — chặn theo TRẠNG THÁI buổi học thay vì khung giờ.
        // Buổi đã kết thúc (đã check-out) → đóng phòng, đá mọi người ra.
        if (classSession.Checkouttime != null)
            return BadRequest(APIResponse<object>.Fail("Buổi học đã kết thúc.", 400));

        // Chỉ mở với buổi "đã lên lịch" hoặc "đang diễn ra". Ẩn/đóng với các trạng thái khác
        // (reserved, pending_confirmation, completed, cancelled, no_show...).
        if (classSession.Status != ClassSessionStatus.Scheduled && classSession.Status != ClassSessionStatus.InProgress)
            return BadRequest(APIResponse<object>.Fail("Phòng học không khả dụng cho buổi này.", 400));

        // Lấy thông tin phòng (channel dùng chung theo booking + token)
        var roomInfo = agoraService.GetRoomInfo(classSessionId, classSession.Bookingid, userId);

        // Tên hai phía chính của lớp học dùng cho tiêu đề phòng. Parent là người tạo/quản lý
        // booking, không thay thế tên học viên khi Student đã có profile riêng.
        var tutorName = classSession.Tutor?.Tutor?.Fullname ?? "Gia sư";
        var studentName = classSession.Booking?.Student?.Fullname ?? "Học sinh";
        var tutorUserId = classSession.Tutorid;
        var parentUserId = classSession.Booking?.Parentid;
        var studentUserId = classSession.Booking?.Student?.Linkeduserid;

        // Bảng tra tên theo Agora UID dùng cho video/chat. Vẫn giữ Parent trong bảng này vì
        // Parent có quyền tham gia phòng và cần hiển thị đúng tên nếu thực sự join.
        var participantNames = new Dictionary<string, string>();
        if (classSession.Tutor?.Tutor != null && !string.IsNullOrEmpty(tutorUserId)) {
            participantNames[tutorUserId] = tutorName;
        }
        if (classSession.Booking?.Parent != null && !string.IsNullOrEmpty(parentUserId)) {
            participantNames[parentUserId] = classSession.Booking.Parent.Fullname ?? "Phụ huynh";
        }
        if (!string.IsNullOrEmpty(studentUserId)) {
            participantNames[studentUserId] = studentName;
        }

        return Ok(APIResponse<object>.Success(new
        {
            channel          = roomInfo.Channel,
            classSessionId   = roomInfo.ClassSessionId,
            uid              = roomInfo.Uid,
            token            = roomInfo.Token,
            appId            = roomInfo.AppId,
            expireAt         = roomInfo.ExpireAt,
            tutorName        = tutorName,
            studentName      = studentName,
            participantNames = participantNames,
            // FE dùng để chặn deep-link: buổi scheduled chưa check-in phải đi qua lobby chờ đủ 2 người.
            status           = classSession.Status,
            checkedIn        = classSession.Checkintime != null
        }, "Lấy thông tin phòng Agora RTC thành công."));
    }

    /// <summary>
    /// POST /api/agora/room/{classSessionId}/recording/start
    /// Bắt đầu ghi hình buổi học — FE gọi khi TUTOR vào lớp (join call thành công).
    /// Chỉ tutor của buổi mới bật được (sai tutor → 404). Idempotent (đã ghi thì bỏ qua).
    /// </summary>
    [HttpPost("room/{classSessionId:int}/recording/start")]
    public async Task<IActionResult> StartRecording(int classSessionId)
    {
        await classSessionService.StartSessionRecordingAsync(classSessionId, UserId);
        return Ok(APIResponse<object>.Success(new { }, "Đã bắt đầu ghi hình buổi học."));
    }

    /// <summary>
    /// GET /api/agora/whiteboard/{classSessionId}
    /// Lấy thông tin để join phòng Interactive Whiteboard (Netless) của buổi học.
    /// Cùng điều kiện truy cập với phòng video: chỉ Tutor/Parent/Student thuộc buổi học,
    /// trong khung giờ phòng mở, và không bị chặn bởi thanh toán còn thiếu.
    /// </summary>
    /// <returns>{ appIdentifier, region, roomUuid, roomToken, role }</returns>
    [HttpGet("whiteboard/{classSessionId:int}")]
    public async Task<IActionResult> GetWhiteboardRoom(int classSessionId)
    {
        var userId = UserId;

        var classSession = await context.ClassSessions
            .Include(l => l.Booking)
            .FirstOrDefaultAsync(l => l.Classsessionid == classSessionId);

        if (classSession == null)
            return NotFound(APIResponse<object>.Fail("Không tìm thấy buổi học.", 404));

        if (!await CheckClassSessionAccessAsync(classSession, userId))
            return Forbid();

        if (CurrentUserRole != UserRole.Admin
            && await classSessionService.IsSessionBlockedByRemainingPaymentAsync(classSessionId))
        {
            return BadRequest(APIResponse<object>.Fail(
                "Phụ huynh chưa thanh toán các buổi học còn lại. Vui lòng hoàn tất thanh toán trước khi vào lớp.", 400));
        }

        var now = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
        if (now < classSession.Scheduledstart.AddMinutes(-30))
            return BadRequest(APIResponse<object>.Fail("Phòng học chưa mở.", 400));
        if (now > classSession.Scheduledend.AddHours(4))
            return BadRequest(APIResponse<object>.Fail("Buổi học đã kết thúc.", 400));

        var isTutor = classSession.Tutorid == userId;
        var room = await whiteboardService.GetOrCreateRoomAsync(classSessionId, isTutor);

        return Ok(APIResponse<object>.Success(new
        {
            appIdentifier = room.AppIdentifier,
            region        = room.Region,
            roomUuid      = room.RoomUuid,
            roomToken     = room.RoomToken,
            role          = room.Role
        }, "Lấy thông tin phòng whiteboard thành công."));
    }

    /// <summary>
    /// POST /api/agora/room/{classSessionId}/heartbeat
    /// Client gọi định kỳ (~20s) khi đang trong phòng để báo "đang có mặt". Presence được ghi
    /// dưới UserId từ JWT (không ai khai hộ được). Sau khi cập nhật, thử auto check-in nếu đủ cả
    /// gia sư và học viên. Trả trạng thái presence + check-in để FE hiển thị / tự rời khi phòng đóng.
    /// </summary>
    [HttpPost("room/{classSessionId:int}/heartbeat")]
    public async Task<IActionResult> Heartbeat(int classSessionId)
    {
        var userId = UserId;

        var classSession = await context.ClassSessions
            .Include(l => l.Booking)
            .FirstOrDefaultAsync(l => l.Classsessionid == classSessionId);

        if (classSession == null)
            return NotFound(APIResponse<object>.Fail("Không tìm thấy buổi học.", 404));

        if (!await CheckClassSessionAccessAsync(classSession, userId))
            return Forbid();

        presence.Heartbeat(classSessionId, userId);

        var status = await classSessionService.TryAutoCheckInAsync(classSessionId);

        return Ok(APIResponse<object>.Success(new
        {
            tutorPresent     = status.TutorPresent,
            studentPresent   = status.StudentPresent,
            isCheckedIn      = status.IsCheckedIn,
            roomClosed       = status.RoomClosed,
            blockedByPayment = status.BlockedByPayment
        }, "OK"));
    }

    /// <summary>
    /// POST /api/agora/room/{classSessionId}/leave
    /// Client gọi khi rời phòng (kể cả beforeunload) để xoá presence ngay, giúp phía kia biết
    /// mình đã rời. Không đổi trạng thái buổi học.
    /// </summary>
    [HttpPost("room/{classSessionId:int}/leave")]
    public IActionResult Leave(int classSessionId)
    {
        presence.Leave(classSessionId, UserId);
        return Ok(APIResponse<object>.Success(new { }, "OK"));
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
