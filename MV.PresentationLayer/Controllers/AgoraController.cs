using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using MV.ApplicationLayer.Helpers;
using MV.ApplicationLayer.Hubs;
using MV.ApplicationLayer.Interfaces;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.Entities;
using MV.PresentationLayer.Helpers;
using System.Security.Claims;

namespace MV.PresentationLayer.Controllers;

/// <summary>
/// Agora RTC Controller — cung cấp token + channel để client join video call.
///
/// Flow hoạt động:
///   1. Client (Tutor/Student/Parent) POST /api/agora/room/{classSessionId}/join với danh tính
///      thiết bị. Chỉ thiết bị giữ lease hiện hành mới nhận token + channel + appId.
///   2. Client join channel bằng Agora SDK: appId + channel + token + uid (= userId).
///   3. Trong lúc ở trong phòng, client heartbeat định kỳ (POST .../heartbeat) → khi cả gia sư
///      lẫn học viên cùng có mặt, backend auto check-in buổi học.
/// </summary>
[ApiController]
[Route("api/agora")]
[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class AgoraController(
    IAgoraRTCService agoraService,
    IClassSessionService classSessionService,
    ISessionPresenceService presence,
    ILiveSessionDeviceLeaseService deviceLeases,
    IWhiteboardService whiteboardService,
    IAppDbContext context,
    IHubContext<LiveSessionHub> liveSessionHub,
    ILogger<AgoraController> logger) : ControllerBase
{
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new UnauthorizedAccessException("Không tìm thấy UserId trong token.");
    private string? CurrentUserRole => User.FindFirstValue("http://schemas.microsoft.com/ws/2008/06/identity/claims/role");

    /// <summary>
    /// Legacy route deliberately does not issue an Agora token. A device admission lease is
    /// mandatory; clients must use POST /join or POST /takeover.
    /// </summary>
    [HttpGet("room/{classSessionId:int}")]
    public IActionResult GetRoomInfo(int classSessionId)
    {
        return BadRequest(APIResponse<object>.Fail(
            "Thiết bị phải đăng ký phiên tham gia trước khi nhận thông tin phòng học.",
            400,
            new { code = LiveSessionDeviceErrorCodes.DeviceSessionRequired }));
    }

    [HttpPost("room/{classSessionId:int}/join")]
    public Task<IActionResult> JoinRoom(
        int classSessionId,
        [FromBody] LiveSessionJoinRequest request,
        CancellationToken cancellationToken)
    {
        return AdmitAndBuildRoomAsync(classSessionId, request, null, cancellationToken);
    }

    [HttpPost("room/{classSessionId:int}/takeover")]
    public Task<IActionResult> TakeOverRoom(
        int classSessionId,
        [FromBody] LiveSessionTakeoverRequest request,
        CancellationToken cancellationToken)
    {
        return AdmitAndBuildRoomAsync(classSessionId, request, request.ExpectedActiveLeaseId, cancellationToken);
    }

    /// <summary>
    /// POST /api/agora/room/{classSessionId}/heartbeat
    /// Client gọi định kỳ (~20s) khi đang trong phòng để báo "đang có mặt". Presence được ghi
    /// dưới UserId từ JWT (không ai khai hộ được). Sau khi cập nhật, thử auto check-in nếu đủ cả
    /// gia sư và học viên. Trả trạng thái presence + check-in để FE hiển thị / tự rời khi phòng đóng.
    /// </summary>
    [HttpPost("room/{classSessionId:int}/heartbeat")]
    public async Task<IActionResult> Heartbeat(
        int classSessionId,
        [FromBody] LiveSessionLeaseRequest request,
        CancellationToken cancellationToken)
    {
        var userId = UserId;
        if (!TryNormalizeLeaseRequest(request, out var participationId, out var leaseId))
            return InvalidDeviceSession();

        var classSession = await context.ClassSessions
            .Include(l => l.Booking)
            .FirstOrDefaultAsync(l => l.Classsessionid == classSessionId);

        if (classSession == null)
            return NotFound(APIResponse<object>.Fail("Không tìm thấy buổi học.", 404));

        if (!await CheckClassSessionAccessAsync(classSession, userId))
            return Forbid();

        if (!await deviceLeases.RenewAsync(
                classSessionId, userId, participationId, leaseId, cancellationToken))
        {
            return RevokedLease();
        }

        presence.Heartbeat(classSessionId, userId);

        var status = await classSessionService.TryAutoCheckInAsync(classSessionId);

        return Ok(APIResponse<object>.Success(new
        {
            tutorPresent     = status.TutorPresent,
            studentPresent   = status.StudentPresent,
            isCheckedIn      = status.IsCheckedIn,
            roomClosed       = status.RoomClosed,
            blockedByPayment = status.BlockedByPayment,
            isRecording      = status.IsRecording
        }, "OK"));
    }

    /// <summary>
    /// POST /api/agora/room/{classSessionId}/leave
    /// Client gọi khi rời phòng (kể cả beforeunload) để xoá presence ngay, giúp phía kia biết
    /// mình đã rời. Không đổi trạng thái buổi học.
    /// </summary>
    [HttpPost("room/{classSessionId:int}/leave")]
    public async Task<IActionResult> Leave(
        int classSessionId,
        [FromBody] LiveSessionLeaseRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryNormalizeLeaseRequest(request, out var participationId, out var leaseId))
            return InvalidDeviceSession();

        var released = await deviceLeases.ReleaseAsync(
            classSessionId, UserId, participationId, leaseId, cancellationToken);
        if (released)
            presence.Leave(classSessionId, UserId);

        return Ok(APIResponse<object>.Success(new { released }, "OK"));
    }

    /// <summary>
    /// POST /api/agora/whiteboard/{classSessionId}
    /// Lấy thông tin để join phòng Interactive Whiteboard (Netless) của buổi học.
    /// Cùng điều kiện truy cập với phòng video: chỉ Tutor/Parent/Student thuộc buổi học,
    /// buổi đang mở (scheduled/in_progress, chưa check-out), không bị chặn bởi thanh toán.
    /// </summary>
    /// <returns>{ appIdentifier, region, roomUuid, roomToken, role }</returns>
    [HttpPost("whiteboard/{classSessionId:int}")]
    public async Task<IActionResult> GetWhiteboardRoom(
        int classSessionId,
        [FromBody] LiveSessionLeaseRequest request,
        CancellationToken cancellationToken)
    {
        var userId = UserId;
        if (!TryNormalizeLeaseRequest(request, out var normalizedParticipationId, out var normalizedLeaseId))
            return InvalidDeviceSession();

        var classSession = await context.ClassSessions
            .Include(l => l.Booking)
            .FirstOrDefaultAsync(l => l.Classsessionid == classSessionId);

        if (classSession == null)
            return NotFound(APIResponse<object>.Fail("Không tìm thấy buổi học.", 404));

        if (!await CheckClassSessionAccessAsync(classSession, userId))
            return Forbid();

        if (!await deviceLeases.IsActiveAsync(
                classSessionId,
                userId,
                normalizedParticipationId,
                normalizedLeaseId,
                cancellationToken))
        {
            return RevokedLease();
        }

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

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task<IActionResult> AdmitAndBuildRoomAsync(
        int classSessionId,
        LiveSessionJoinRequest request,
        string? expectedActiveLeaseId,
        CancellationToken cancellationToken)
    {
        if (!TryNormalizeJoinRequest(
                request,
                out var participationId,
                out var deviceId,
                out var deviceLabel))
        {
            return InvalidDeviceSession();
        }

        string? normalizedExpectedLeaseId = null;
        if (expectedActiveLeaseId != null
            && !LiveSessionDeviceInputValidator.TryNormalizeGuid(
                expectedActiveLeaseId,
                out normalizedExpectedLeaseId))
        {
            return InvalidDeviceSession();
        }

        var userId = UserId;
        var classSession = await context.ClassSessions
            .Include(l => l.Tutor!)
                .ThenInclude(t => t.Tutor)
            .Include(l => l.Booking)
                .ThenInclude(b => b!.Parent)
            .Include(l => l.Booking)
                .ThenInclude(b => b!.Student)
            .FirstOrDefaultAsync(l => l.Classsessionid == classSessionId, cancellationToken);

        if (classSession == null)
            return NotFound(APIResponse<object>.Fail("Không tìm thấy buổi học.", 404));

        if (!await CheckClassSessionAccessAsync(classSession, userId))
            return Forbid();

        if (CurrentUserRole != UserRole.Admin
            && await classSessionService.IsSessionBlockedByRemainingPaymentAsync(classSessionId))
        {
            return BadRequest(APIResponse<object>.Fail(
                "Phụ huynh chưa thanh toán các buổi học còn lại. Vui lòng hoàn tất thanh toán trước khi vào lớp buổi tiếp theo.",
                400));
        }

        if (classSession.Checkouttime != null)
            return BadRequest(APIResponse<object>.Fail("Buổi học đã kết thúc.", 400));

        if (classSession.Status != ClassSessionStatus.Scheduled
            && classSession.Status != ClassSessionStatus.InProgress)
        {
            return BadRequest(APIResponse<object>.Fail("Phòng học không khả dụng cho buổi này.", 400));
        }

        LiveSessionDeviceLease lease;
        if (normalizedExpectedLeaseId == null)
        {
            var admission = await deviceLeases.AdmitAsync(
                classSessionId,
                userId,
                participationId,
                deviceId,
                deviceLabel,
                cancellationToken);

            if (!admission.Admitted || admission.Lease == null)
                return ActiveDeviceConflict(admission.ActiveLeaseId, admission.ActiveDeviceLabel);

            lease = admission.Lease;
        }
        else
        {
            var takeover = await deviceLeases.TakeoverAsync(
                classSessionId,
                userId,
                participationId,
                deviceId,
                deviceLabel,
                normalizedExpectedLeaseId,
                cancellationToken);

            if (!takeover.TakenOver || takeover.Lease == null)
                return ActiveDeviceConflict(takeover.ActiveLeaseId, takeover.ActiveDeviceLabel);

            lease = takeover.Lease;

            if (!string.IsNullOrEmpty(takeover.ReplacedLeaseId))
            {
                try
                {
                    await liveSessionHub.Clients
                        .Group(LiveSessionHub.LeaseGroup(takeover.ReplacedLeaseId))
                        .SendAsync("sessionReplaced", new
                        {
                            classSessionId,
                            leaseId = takeover.ReplacedLeaseId,
                            reason = "taken_over"
                        }, cancellationToken);
                }
                catch (Exception ex)
                {
                    // The lease CAS is already committed. Heartbeat rejection remains the
                    // authoritative fallback if the best-effort real-time signal cannot be sent.
                    logger.LogWarning(
                        ex,
                        "Could not signal replaced live-session device for user {UserId}, classSession {ClassSessionId}",
                        userId,
                        classSessionId);
                }
            }
        }

        var roomInfo = agoraService.GetRoomInfo(classSessionId, classSession.Bookingid, userId);
        var tutorName = classSession.Tutor?.Tutor?.Fullname ?? "Gia sư";
        var studentName = classSession.Booking?.Student?.Fullname ?? "Học sinh";
        var tutorUserId = classSession.Tutorid;
        var parentUserId = classSession.Booking?.Parentid;
        var studentUserId = classSession.Booking?.Student?.Linkeduserid;
        var participantNames = new Dictionary<string, string>();

        if (classSession.Tutor?.Tutor != null && !string.IsNullOrEmpty(tutorUserId))
            participantNames[tutorUserId] = tutorName;
        if (classSession.Booking?.Parent != null && !string.IsNullOrEmpty(parentUserId))
            participantNames[parentUserId] = classSession.Booking.Parent.Fullname ?? "Phụ huynh";
        if (!string.IsNullOrEmpty(studentUserId))
            participantNames[studentUserId] = studentName;

        return Ok(APIResponse<object>.Success(new
        {
            channel = roomInfo.Channel,
            classSessionId = roomInfo.ClassSessionId,
            uid = roomInfo.Uid,
            token = roomInfo.Token,
            appId = roomInfo.AppId,
            expireAt = roomInfo.ExpireAt,
            tutorName,
            studentName,
            participantNames,
            status = classSession.Status,
            checkedIn = classSession.Checkintime != null,
            participationId = lease.ParticipationId,
            leaseId = lease.LeaseId
        }, "Lấy thông tin phòng Agora RTC thành công."));
    }

    private IActionResult ActiveDeviceConflict(string? activeLeaseId, string? activeDeviceLabel)
    {
        return Conflict(APIResponse<object>.Fail(
            "Bạn đã tham gia buổi học trên một thiết bị khác.",
            409,
            new
            {
                code = LiveSessionDeviceErrorCodes.ActiveOnAnotherDevice,
                activeLeaseId,
                activeDeviceLabel
            }));
    }

    private IActionResult RevokedLease()
    {
        return Conflict(APIResponse<object>.Fail(
            "Phiên tham gia trên thiết bị này đã được thay thế.",
            409,
            new { code = LiveSessionDeviceErrorCodes.LeaseRevoked }));
    }

    private IActionResult InvalidDeviceSession()
    {
        return BadRequest(APIResponse<object>.Fail(
            "Thông tin phiên hoặc thiết bị không hợp lệ.",
            400,
            new { code = LiveSessionDeviceErrorCodes.InvalidDeviceSession }));
    }

    private static bool TryNormalizeJoinRequest(
        LiveSessionJoinRequest request,
        out string participationId,
        out string deviceId,
        out string deviceLabel)
    {
        participationId = string.Empty;
        deviceId = string.Empty;
        deviceLabel = string.Empty;
        return LiveSessionDeviceInputValidator.TryNormalizeGuid(request.ParticipationId, out participationId)
            && LiveSessionDeviceInputValidator.TryNormalizeGuid(request.DeviceId, out deviceId)
            && LiveSessionDeviceInputValidator.TryNormalizeDeviceLabel(request.DeviceLabel, out deviceLabel);
    }

    private static bool TryNormalizeLeaseRequest(
        LiveSessionLeaseRequest request,
        out string participationId,
        out string leaseId)
    {
        participationId = string.Empty;
        leaseId = string.Empty;
        return LiveSessionDeviceInputValidator.TryNormalizeGuid(request.ParticipationId, out participationId)
            && LiveSessionDeviceInputValidator.TryNormalizeGuid(request.LeaseId, out leaseId);
    }

    private async Task<bool> CheckClassSessionAccessAsync(ClassSession classSession, string userId)
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
