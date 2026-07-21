using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using MV.ApplicationLayer.Hubs;
using MV.ApplicationLayer.Interfaces;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.Entities;
using MV.DomainLayer.Helpers;
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
    ISessionPresenceService presence,
    IWhiteboardService whiteboardService,
    IHubContext<NotificationHub> notificationHub,
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
    /// GET /api/agora/whiteboard/{classSessionId}
    /// Lấy thông tin để join phòng Interactive Whiteboard (Netless) của buổi học.
    /// Cùng điều kiện truy cập với phòng video: chỉ Tutor/Parent/Student thuộc buổi học,
    /// buổi đang mở (scheduled/in_progress, chưa check-out), không bị chặn bởi thanh toán.
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

        // Cùng chính sách mở phòng với GetRoomInfo: theo TRẠNG THÁI buổi học, không theo khung giờ.
        if (classSession.Checkouttime != null)
            return BadRequest(APIResponse<object>.Fail("Buổi học đã kết thúc.", 400));
        if (classSession.Status != ClassSessionStatus.Scheduled && classSession.Status != ClassSessionStatus.InProgress)
            return BadRequest(APIResponse<object>.Fail("Phòng học không khả dụng cho buổi này.", 400));

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

    // ── Emotion / Engagement monitoring ───────────────────────────────────────
    // Máy HỌC VIÊN tự phân tích cảm xúc/độ tập trung cục bộ (MediaPipe + FER+ trong trình duyệt).
    // Ảnh khuôn mặt KHÔNG rời máy học viên — chỉ điểm số/nhãn đã tổng hợp mới gửi lên đây.

    /// <summary>
    /// POST /api/agora/room/{classSessionId}/engagement
    /// Học viên gửi lô mẫu điểm cảm xúc/độ tập trung (~mỗi 15-30s) để lưu, phục vụ báo cáo sau buổi.
    /// Chỉ HỌC VIÊN của buổi học được gửi (gia sư/phụ huynh không tự sinh dữ liệu này).
    /// </summary>
    [HttpPost("room/{classSessionId:int}/engagement")]
    public async Task<IActionResult> IngestEngagement(int classSessionId, [FromBody] EngagementSampleBatchRequest body)
    {
        var userId = UserId;

        var classSession = await context.ClassSessions
            .Include(l => l.Booking)
            .FirstOrDefaultAsync(l => l.Classsessionid == classSessionId);

        if (classSession == null)
            return NotFound(APIResponse<object>.Fail("Không tìm thấy buổi học.", 404));

        // Chỉ chính học viên của buổi mới được gửi mẫu (không phải bất kỳ ai có quyền truy cập).
        if (!await IsSessionStudentAsync(classSession, userId))
            return Forbid();

        if (body?.Samples == null || body.Samples.Count == 0)
            return Ok(APIResponse<object>.Success(new { inserted = 0 }, "Không có mẫu nào để lưu."));

        var now = TimeZoneHelper.UtcNow;
        var rows = body.Samples.Select(s => new SessionEngagementSample
        {
            ClassSessionId  = classSessionId,
            StudentUserId   = userId,
            Emotion         = s.Emotion,
            EngagementScore = s.EngagementScore,
            Drowsy          = s.Drowsy,
            SampledAt       = now,
        }).ToList();

        context.SessionEngagementSamples.AddRange(rows);
        await context.SaveChangesAsync();

        return Ok(APIResponse<object>.Success(new { inserted = rows.Count }, "Đã lưu mẫu độ tập trung."));
    }

    /// <summary>
    /// POST /api/agora/room/{classSessionId}/engagement-alert
    /// Học viên báo một cảnh báo (rời màn hình / buồn ngủ / mất tập trung / căng thẳng). BE lưu lại
    /// kèm mẫu và đẩy realtime tới GIA SƯ của buổi qua SignalR để hiện toast.
    /// </summary>
    [HttpPost("room/{classSessionId:int}/engagement-alert")]
    public async Task<IActionResult> EngagementAlert(int classSessionId, [FromBody] EngagementAlertRequest body)
    {
        var userId = UserId;

        var classSession = await context.ClassSessions
            .Include(l => l.Booking)
            .FirstOrDefaultAsync(l => l.Classsessionid == classSessionId);

        if (classSession == null)
            return NotFound(APIResponse<object>.Fail("Không tìm thấy buổi học.", 404));

        if (!await IsSessionStudentAsync(classSession, userId))
            return Forbid();

        if (body == null || string.IsNullOrWhiteSpace(body.Reason) || string.IsNullOrWhiteSpace(body.Message))
            return BadRequest(APIResponse<object>.Fail("Thiếu 'reason' hoặc 'message'.", 400));

        // Lưu alert như một mẫu có alert_reason để báo cáo đếm được số cảnh báo mà không cần bảng riêng.
        context.SessionEngagementSamples.Add(new SessionEngagementSample
        {
            ClassSessionId  = classSessionId,
            StudentUserId   = userId,
            Emotion         = null,
            EngagementScore = body.EngagementScore ?? 0,
            Drowsy          = body.Reason == "drowsy",
            AlertReason     = body.Reason,
            SampledAt       = TimeZoneHelper.UtcNow,
        });
        await context.SaveChangesAsync();

        // Đẩy tới gia sư qua NotificationHub, group "user:{tutorUserId}" — BaseHub.OnConnectedAsync
        // tự thêm mọi connection vào group này. Dùng NotificationHub (không phải SessionLobbyHub) vì
        // gia sư luôn kết nối NotificationHub toàn app; SessionLobbyHub chỉ sống ở trang phòng chờ.
        var tutorUserId = classSession.Tutorid;
        if (!string.IsNullOrEmpty(tutorUserId))
        {
            await notificationHub.Clients.Group($"user:{tutorUserId}").SendAsync("engagementAlert", new
            {
                classSessionId,
                reason  = body.Reason,
                level   = body.Level,
                message = body.Message,
            });
        }

        return Ok(APIResponse<object>.Success(new { }, "Đã gửi cảnh báo."));
    }

    /// <summary>
    /// GET /api/agora/room/{classSessionId}/engagement/summary
    /// Tổng hợp cho báo cáo sau buổi: điểm tập trung trung bình, phân bố cảm xúc, số cảnh báo.
    /// Tutor / Parent / Student của buổi đều xem được.
    /// </summary>
    [HttpGet("room/{classSessionId:int}/engagement/summary")]
    public async Task<IActionResult> EngagementSummary(int classSessionId)
    {
        var userId = UserId;

        var classSession = await context.ClassSessions
            .Include(l => l.Booking)
            .FirstOrDefaultAsync(l => l.Classsessionid == classSessionId);

        if (classSession == null)
            return NotFound(APIResponse<object>.Fail("Không tìm thấy buổi học.", 404));

        if (!await CheckClassSessionAccessAsync(classSession, userId))
            return Forbid();

        var samples = await context.SessionEngagementSamples
            .AsNoTracking()
            .Where(s => s.ClassSessionId == classSessionId)
            .OrderBy(s => s.SampledAt)
            .ToListAsync();

        // Chỉ các mẫu điểm thường (không phải alert) mới tính vào trung bình / phân bố cảm xúc.
        var scoreSamples = samples.Where(s => s.AlertReason == null).ToList();
        var avgEngagement = scoreSamples.Count > 0
            ? Math.Round(scoreSamples.Average(s => s.EngagementScore), 4)
            : 0d;

        var emotionDistribution = scoreSamples
            .Where(s => !string.IsNullOrEmpty(s.Emotion))
            .GroupBy(s => s.Emotion!)
            .ToDictionary(g => g.Key, g => g.Count());

        var alertCounts = samples
            .Where(s => s.AlertReason != null)
            .GroupBy(s => s.AlertReason!)
            .ToDictionary(g => g.Key, g => g.Count());

        // Chuỗi thời gian điểm tập trung (cho biểu đồ) — chỉ mẫu điểm thường.
        var timeline = scoreSamples.Select(s => new
        {
            sampledAt       = s.SampledAt,
            engagementScore = s.EngagementScore,
            emotion         = s.Emotion,
            drowsy          = s.Drowsy,
        });

        return Ok(APIResponse<object>.Success(new
        {
            classSessionId,
            sampleCount        = scoreSamples.Count,
            avgEngagement,
            emotionDistribution,
            alertCounts,
            totalAlerts        = samples.Count(s => s.AlertReason != null),
            timeline,
        }, "Lấy tổng hợp độ tập trung thành công."));
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// True nếu <paramref name="userId"/> chính là HỌC VIÊN của buổi học (tài khoản student có
    /// Linkeduserid). Chặt hơn <see cref="CheckClassSessionAccessAsync"/> — không tính tutor/parent/admin.
    /// </summary>
    private async Task<bool> IsSessionStudentAsync(ClassSession classSession, string userId)
    {
        var studentId = classSession.Booking?.Studentid;
        if (string.IsNullOrEmpty(studentId))
            return false;

        var linkedUserId = await context.Studentprofiles
            .Where(s => s.Studentid == studentId)
            .Select(s => s.Linkeduserid)
            .FirstOrDefaultAsync();

        return linkedUserId == userId;
    }

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
