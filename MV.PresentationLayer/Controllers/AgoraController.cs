using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MV.ApplicationLayer.Helpers;
using MV.ApplicationLayer.Hubs;
using MV.ApplicationLayer.Interfaces;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Configuration;
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
///   1. Client (Tutor/Student) POST /api/agora/room/{classSessionId}/join với danh tính
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
    IClassSessionScheduleChangeService scheduleChanges,
    ISessionPresenceService presence,
    ISessionLobbyPresenceBroadcaster lobbyPresenceBroadcaster,
    ILiveSessionDeviceLeaseService deviceLeases,
    IWhiteboardService whiteboardService,
    ISessionLogService sessionLogService,
    IAppDbContext context,
    IHubContext<LiveSessionHub> liveSessionHub,
    IHubContext<NotificationHub> notificationHub,
    IOptions<SessionEvidenceSettings> evidenceSettings,
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
                .ThenInclude(b => b!.Student)
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

        var heartbeatScheduleState = await scheduleChanges.GetOrCreateStateAsync(
            classSessionId,
            userId,
            CurrentUserRole,
            cancellationToken);
        if (!heartbeatScheduleState.AdmissionAllowed)
        {
            presence.Leave(classSessionId, userId);
            return Conflict(APIResponse<object>.Fail(
                "Cần đủ xác nhận đổi giờ học trước khi vào phòng.",
                409,
                new
                {
                    code = LiveSessionScheduleChangeErrorCodes.ConfirmationRequired,
                    scheduleChange = heartbeatScheduleState
                }));
        }
        var heartbeatScheduleConflict = await scheduleChanges.FindCurrentConflictAsync(
            classSessionId,
            TimeZoneHelper.UtcNow,
            cancellationToken);
        if (heartbeatScheduleConflict != null)
        {
            presence.Leave(classSessionId, userId);
            return Conflict(APIResponse<object>.Fail(
                heartbeatScheduleConflict.Message,
                409,
                new
                {
                    code = LiveSessionScheduleChangeErrorCodes.ScheduleConflict,
                    conflict = heartbeatScheduleConflict
                }));
        }

        // Chụp trước khi ghi: chỉ khi đây là lần "vừa có mặt" (chưa present ở lần gọi trước) mới cần
        // báo cho lobby — tránh gọi lại broadcaster (có truy vấn DB + GetOrCreateStateAsync) mỗi 20s
        // suốt cả buổi học một khi cả hai đã ổn định trong phòng.
        var wasPresent = presence.IsPresent(classSessionId, userId);
        presence.Heartbeat(classSessionId, userId);

        // Phía còn lại có thể vẫn đang đứng trong SessionLobbyHub (chưa qua đây) — trước đây chỉ
        // biết tin này qua RefreshState (~10s, chỉ là lưới an toàn), nên nếu poll đó lỡ nhịp thì họ
        // đứng nhìn banner "chưa vào" vô thời hạn dù mình đã vào phòng thật rồi.
        if (!wasPresent)
        {
            await lobbyPresenceBroadcaster.BroadcastAsync(classSessionId, userId, CurrentUserRole, cancellationToken);
        }

        // In-memory presence answers "is this lesson checkable in right now" and is gone on restart.
        // The same beat is also appended to the durable chain, which is what a dispute weeks later
        // is settled with. Best-effort inside the service: it must never break the beat.
        await sessionLogService.RecordHeartbeatAsync(
            classSessionId,
            userId,
            ResolveParticipantRole(classSession, userId),
            request.Activity,
            cancellationToken);

        var status = await classSessionService.TryAutoCheckInAsync(classSessionId);
        if (status.ScheduleConflict != null)
        {
            presence.Leave(classSessionId, userId);
            return Conflict(APIResponse<object>.Fail(
                status.ScheduleConflict.Message,
                409,
                new
                {
                    code = LiveSessionScheduleChangeErrorCodes.ScheduleConflict,
                    conflict = status.ScheduleConflict
                }));
        }

        return Ok(APIResponse<object>.Success(new
        {
            tutorPresent     = status.TutorPresent,
            studentPresent   = status.StudentPresent,
            isCheckedIn      = status.IsCheckedIn,
            roomClosed       = status.RoomClosed,
            blockedByPayment = status.BlockedByPayment,
            isRecording      = status.IsRecording,
            autoEndAt        = status.AutoEndAt
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
        {
            presence.Leave(classSessionId, UserId);
            // Marks the beat run as an intentional exit, so leaving on purpose stays
            // distinguishable from a client that simply stopped reporting.
            await sessionLogService.CloseHeartbeatAsync(classSessionId, UserId, cancellationToken);
        }

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

        var whiteboardScheduleState = await scheduleChanges.GetOrCreateStateAsync(
            classSessionId,
            userId,
            CurrentUserRole,
            cancellationToken);
        if (!whiteboardScheduleState.AdmissionAllowed)
        {
            return Conflict(APIResponse<object>.Fail(
                "Cần đủ xác nhận đổi giờ học trước khi mở bảng trắng.",
                409,
                new { code = LiveSessionScheduleChangeErrorCodes.ConfirmationRequired }));
        }
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

        var scheduleChangeState = await scheduleChanges.GetOrCreateStateAsync(
            classSessionId,
            userId,
            CurrentUserRole,
            cancellationToken);
        if (!scheduleChangeState.AdmissionAllowed)
        {
            return Conflict(APIResponse<object>.Fail(
                "Cần đủ xác nhận đổi giờ học trước khi vào phòng.",
                409,
                new
                {
                    code = LiveSessionScheduleChangeErrorCodes.ConfirmationRequired,
                    scheduleChange = scheduleChangeState
                }));
        }
        var scheduleConflict = await scheduleChanges.FindCurrentConflictAsync(
            classSessionId,
            TimeZoneHelper.UtcNow,
            cancellationToken);
        if (scheduleConflict != null)
        {
            return Conflict(APIResponse<object>.Fail(
                scheduleConflict.Message,
                409,
                new
                {
                    code = LiveSessionScheduleChangeErrorCodes.ScheduleConflict,
                    conflict = scheduleConflict
                }));
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
        var studentUserId = classSession.Booking?.Student?.Linkeduserid;
        var participantNames = new Dictionary<string, string>();

        // Agora's channel notifications identify users by a uid that carries no application
        // meaning, so the session log needs to know who was admitted and when. The network and
        // device come along for the ride: two of them for one account in one lesson is the signal
        // behind account sharing. Recording it is best-effort inside the service — it must never
        // stand between a user and their lesson.
        await sessionLogService.RecordAdmissionAsync(
            classSessionId,
            userId,
            ResolveParticipantRole(classSession, userId),
            new SessionAdmissionContext(
                ResolveClientIpAddress(),
                deviceId,
                deviceLabel,
                Request.Headers.UserAgent.ToString()),
            cancellationToken);

        if (classSession.Tutor?.Tutor != null && !string.IsNullOrEmpty(tutorUserId))
            participantNames[tutorUserId] = tutorName;
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
            // Mốc chung để đồng hồ 2 màn hình khớp nhau (xem AgoraRoomInfo.startedAt/LiveSession
            // index.tsx) — trước đây chưa từng gửi field này, nên FE luôn fallback về đếm cục bộ
            // từ lúc THIẾT BỊ ĐÓ tự join, và reset lại về 0 mỗi khi 1 bên rớt mạng rồi vào lại.
            startedAt = classSession.Checkintime,
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

    /// <summary>
    /// Which side of the lesson a user is on, for the attendance record. Requires the booking's
    /// student to be loaded; an unloaded one yields <c>unknown</c>, which the session log then
    /// resolves from the booking itself rather than guessing.
    /// </summary>
    private static string ResolveParticipantRole(ClassSession classSession, string userId)
    {
        if (userId == classSession.Tutorid) return SessionParticipantRole.Tutor;
        if (userId == classSession.Booking?.Student?.Linkeduserid) return SessionParticipantRole.Student;
        if (userId == classSession.Booking?.Parentid) return SessionParticipantRole.Parent;
        return SessionParticipantRole.Unknown;
    }

    /// <summary>
    /// The client address to store with an admission.
    ///
    /// <c>X-Forwarded-For</c> is only read when the deployment says a trusted proxy overwrites it:
    /// the header is otherwise attacker-controlled, and an address a user chose for themselves would
    /// be worse in the fraud record than no address at all. Returns null when nothing is resolvable.
    /// </summary>
    private string? ResolveClientIpAddress()
    {
        if (evidenceSettings.Value.TrustForwardedFor)
        {
            var forwarded = Request.Headers["X-Forwarded-For"].FirstOrDefault();
            // Proxies append, so the original client is the leftmost entry.
            var firstHop = forwarded?.Split(',').FirstOrDefault()?.Trim();
            if (!string.IsNullOrWhiteSpace(firstHop))
                return firstHop;
        }

        return HttpContext.Connection.RemoteIpAddress?.ToString();
    }

    private async Task<bool> CheckClassSessionAccessAsync(ClassSession classSession, string userId)
    {
        // Admin có toàn quyền
        if (CurrentUserRole == MV.DomainLayer.Constants.UserRole.Admin)
            return true;

        // Tutor của buổi học
        if (classSession.Tutorid == userId)
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
