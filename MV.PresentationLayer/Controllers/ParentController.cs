using MV.DomainLayer.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.DTO;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Exceptions;
using MV.PresentationLayer.Helpers;
using System.Security.Claims;

namespace MV.PresentationLayer.Controllers;

/// <summary>
/// Controller for parent — classSession management and student management
/// </summary>
[ApiController]
[Route("api/parent")]
[Authorize]
public class ParentController : ControllerBase
{
    private readonly IParentService _parentService;
    private readonly IStudentService _studentService;
    private readonly IClassSessionService _classSessionService;
    private readonly IDisputeService _disputeService;
    private readonly IClassSessionScheduleChangeService _scheduleChangeService;
    private readonly IClassSessionRescheduleProposalService _rescheduleProposalService;
    private readonly IBookingService _bookingService;
    private readonly ISessionLobbyPresenceBroadcaster _lobbyPresenceBroadcaster;
    private readonly ILogger<ParentController> _logger;

    public ParentController(
        IParentService parentService,
        IStudentService studentService,
        IClassSessionService classSessionService,
        IDisputeService disputeService,
        IClassSessionScheduleChangeService scheduleChangeService,
        IClassSessionRescheduleProposalService rescheduleProposalService,
        IBookingService bookingService,
        ISessionLobbyPresenceBroadcaster lobbyPresenceBroadcaster,
        ILogger<ParentController> logger)
    {
        _bookingService = bookingService;
        _parentService = parentService;
        _studentService = studentService;
        _classSessionService = classSessionService;
        _disputeService = disputeService;
        _scheduleChangeService = scheduleChangeService;
        _rescheduleProposalService = rescheduleProposalService;
        _lobbyPresenceBroadcaster = lobbyPresenceBroadcaster;
        _logger = logger;
    }

    private string GetParentId() => User.FindFirstValue(ClaimTypes.NameIdentifier)!;


    // =====================================================================
    // LESSON MANAGEMENT
    // =====================================================================

    /// <summary>
    /// Get classSessions pending confirmation
    /// </summary>
    [HttpGet("class-sessions/pending")]
    [Authorize(Roles = UserRole.ParentOrStudent)]
    public async Task<ActionResult<APIResponse<List<PendingClassSessionResponse>>>> GetPendingClassSessions()
    {
        try
        {
            var userId = UserHelper.GetUserId(User);
            var role = User.FindFirstValue(ClaimTypes.Role) ?? "";
            _logger.LogInformation("GetPendingClassSessions called for userId: {UserId}, role: {Role}", userId, role);
            var result = await _parentService.GetPendingClassSessionsAsync(userId, role);
            return Ok(APIResponse<List<PendingClassSessionResponse>>.Success(result, "Lấy danh sách buổi học chờ xác nhận thành công."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetPendingClassSessions FAILED: {Message}", ex.Message);
            return StatusCode(500, APIResponse<List<PendingClassSessionResponse>>.Fail($"Lỗi hệ thống: {ex.Message}"));
        }
    }

    /// <summary>
    /// Get classSession detail
    /// </summary>
    [HttpGet("class-sessions/{id}")]
    [Authorize(Roles = UserRole.ParentOrStudent)]
    public async Task<ActionResult<APIResponse<ClassSessionDetailResponse>>> GetClassSessionDetail(int id)
    {
        var userId = UserHelper.GetUserId(User);
        var role = User.FindFirstValue(ClaimTypes.Role) ?? "";
        var result = await _parentService.GetClassSessionDetailAsync(id, userId, role);

        if (result == null)
            return NotFound(APIResponse<ClassSessionDetailResponse>.Fail(ApiMessages.ClassSessionNotFound));

        return Ok(APIResponse<ClassSessionDetailResponse>.Success(result, "Lấy chi tiết buổi học thành công."));
    }

    /// <summary>
    /// Parent reads an already-created off-schedule confirmation request from lesson detail.
    /// Merely opening the detail page never creates a new request.
    /// </summary>
    [HttpGet("class-sessions/{id}/schedule-change")]
    [Authorize(Roles = UserRole.Parent)]
    public async Task<ActionResult<APIResponse<SessionScheduleChangeResponse>>> GetScheduleChange(int id)
    {
        var userId = UserHelper.GetUserId(User);
        try
        {
            var result = await _scheduleChangeService.GetExistingStateAsync(id, userId, UserRole.Parent);
            return Ok(APIResponse<SessionScheduleChangeResponse>.Success(result, "Lấy trạng thái xác nhận đổi lịch thành công."));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(APIResponse<object>.Fail(ex.Message, 404));
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, APIResponse<object>.Fail(ex.Message, 403));
        }
    }

    /// <summary>
    /// Parent confirms or rejects an existing off-schedule request without entering the lobby.
    /// </summary>
    [HttpPost("class-sessions/{id}/schedule-change/respond")]
    [Authorize(Roles = UserRole.Parent)]
    public async Task<ActionResult<APIResponse<SessionScheduleChangeResponse>>> RespondToScheduleChange(
        int id,
        [FromBody] SessionScheduleChangeDecisionRequest request)
    {
        var userId = UserHelper.GetUserId(User);
        try
        {
            var existing = await _scheduleChangeService.GetExistingStateAsync(id, userId, UserRole.Parent);
            if (!existing.RequiresConfirmation || existing.Status != ScheduleChangeStatus.Pending)
                return BadRequest(APIResponse<object>.Fail("Yêu cầu đổi lịch không còn chờ xác nhận.", 400));

            var result = await _scheduleChangeService.RespondAsync(
                id,
                userId,
                UserRole.Parent,
                request.Confirmed);

            // Phụ huynh không vào SessionLobbyHub (chỉ gia sư/học sinh của booking mới join lobby) nên
            // phản hồi REST này không tự phát tới group lobby như khi phản hồi qua Hub — đẩy tay để gia
            // sư đang chờ trong lobby thấy ngay thay vì phải reload/đợi RefreshState (~10s).
            await _lobbyPresenceBroadcaster.BroadcastAsync(id, userId, UserRole.Parent, HttpContext.RequestAborted);

            return Ok(APIResponse<SessionScheduleChangeResponse>.Success(
                result,
                request.Confirmed ? "Đã xác nhận đổi lịch học." : "Đã từ chối đổi lịch học."));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(APIResponse<object>.Fail(ex.Message, 404));
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, APIResponse<object>.Fail(ex.Message, 403));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(APIResponse<object>.Fail(ex.Message, 400));
        }
    }

    /// <summary>
    /// Phụ huynh đề xuất dời buổi học sang giờ khác (thay cho con do phụ huynh quản lý).
    /// </summary>
    [HttpPost("class-sessions/{id}/reschedule-proposal")]
    [Authorize(Roles = UserRole.Parent)]
    public async Task<ActionResult<APIResponse<ClassSessionRescheduleProposalResponse>>> ProposeReschedule(
        int id,
        [FromBody] CreateRescheduleProposalRequest request)
    {
        var userId = UserHelper.GetUserId(User);
        try
        {
            var result = await _rescheduleProposalService.ProposeAsync(
                id, userId, UserRole.Parent, request.ProposedScheduledStart, request.Reason);
            return Ok(APIResponse<ClassSessionRescheduleProposalResponse>.Success(result, "Đã gửi đề xuất đổi lịch."));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(APIResponse<object>.Fail(ex.Message, 404));
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, APIResponse<object>.Fail(ex.Message, 403));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(APIResponse<object>.Fail(ex.Message, 400));
        }
    }

    /// <summary>
    /// Phụ huynh đồng ý/từ chối đề xuất đổi lịch đang chờ do gia sư gửi.
    /// </summary>
    [HttpPost("class-sessions/{id}/reschedule-proposal/respond")]
    [Authorize(Roles = UserRole.Parent)]
    public async Task<ActionResult<APIResponse<ClassSessionRescheduleProposalResponse>>> RespondToReschedule(
        int id,
        [FromBody] RescheduleProposalDecisionRequest request)
    {
        var userId = UserHelper.GetUserId(User);
        try
        {
            var result = await _rescheduleProposalService.RespondAsync(id, userId, UserRole.Parent, request.Accepted);
            return Ok(APIResponse<ClassSessionRescheduleProposalResponse>.Success(
                result,
                request.Accepted ? "Đã đồng ý đổi lịch học." : "Đã từ chối đổi lịch học."));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(APIResponse<object>.Fail(ex.Message, 404));
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, APIResponse<object>.Fail(ex.Message, 403));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(APIResponse<object>.Fail(ex.Message, 400));
        }
    }

    /// <summary>
    /// Confirm a classSession as completed
    /// </summary>
    [HttpPut("class-sessions/{id}/confirm")]
    [Authorize(Roles = UserRole.ParentOrStudent)]
    public async Task<ActionResult<APIResponse<SettlementResultResponse>>> ConfirmClassSession(int id)
    {
        var userId = UserHelper.GetUserId(User);
        var role = User.FindFirstValue(ClaimTypes.Role) ?? "";
        var result = await _parentService.ConfirmClassSessionAsync(id, userId, role);
        return Ok(APIResponse<SettlementResultResponse>.Success(result, "Xác nhận buổi học thành công."));
    }

    /// <summary>
    /// Create a dispute for a classSession (no_show / quality / payment / other).
    /// ClassSession must be PendingConfirmation or Completed, and not already disputed.
    /// </summary>
    [HttpPost("class-sessions/{id}/dispute")]
    [Consumes("multipart/form-data")]
    [Authorize(Roles = UserRole.ParentOrStudent)]
    public async Task<ActionResult<APIResponse<DisputeDetailResponse>>> CreateDispute(int id, [FromForm] CreateDisputeRequest request)
    {
        var userId = UserHelper.GetUserId(User);
        var role = User.FindFirstValue(ClaimTypes.Role) ?? "";
        try
        {
            var result = await _parentService.CreateDisputeAsync(id, userId, role, request);
            return Ok(APIResponse<DisputeDetailResponse>.Success(result, "Đã gửi khiếu nại thành công."));
        }
        catch (ClassSessionException ex)
        {
            return StatusCode(ex.HttpStatus, APIResponse<object>.Fail(ex.Message, ex.HttpStatus));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(APIResponse<object>.Fail(ex.Message, 400));
        }
    }

    /// <summary>
    /// Nộp phản hồi cho 1 tranh chấp do GIA SƯ tạo nhắm vào buổi học của phụ huynh/học sinh —
    /// đối xứng với TutorClassSessionController's SubmitDisputeResponse.
    /// </summary>
    [HttpPost("class-sessions/{id}/dispute/response")]
    [Authorize(Roles = UserRole.ParentOrStudent)]
    public async Task<ActionResult<APIResponse<DisputeDetailResponse>>> SubmitDisputeResponse(int id, [FromBody] SubmitTutorResponseRequest request)
    {
        var userId = UserHelper.GetUserId(User);
        var role = User.FindFirstValue(ClaimTypes.Role) ?? "";
        try
        {
            var result = await _disputeService.SubmitRespondentResponseAsync(id, userId, role, request.Response);
            return Ok(APIResponse<DisputeDetailResponse>.Success(result, "Gửi phản hồi thành công."));
        }
        catch (ArgumentException ex)
        {
            return NotFound(APIResponse<DisputeDetailResponse>.Fail(ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(APIResponse<DisputeDetailResponse>.Fail(ex.Message));
        }
    }

    /// <summary>
    /// Nộp bằng chứng cho 1 tranh chấp do GIA SƯ tạo nhắm vào buổi học của phụ huynh/học sinh —
    /// đối xứng với TutorClassSessionController's UploadDisputeEvidence.
    /// </summary>
    [HttpPost("class-sessions/{id}/dispute/evidence")]
    [Authorize(Roles = UserRole.ParentOrStudent)]
    public async Task<ActionResult<APIResponse<string>>> UploadDisputeEvidence(int id, IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(APIResponse<string>.Fail("Tệp bằng chứng là bắt buộc."));

        var userId = UserHelper.GetUserId(User);
        var role = User.FindFirstValue(ClaimTypes.Role) ?? "";
        try
        {
            var result = await _disputeService.UploadRespondentDisputeEvidenceAsync(id, userId, role, file);
            return Ok(APIResponse<string>.Success(result, "Tải tệp bằng chứng thành công."));
        }
        catch (ArgumentException ex)
        {
            return NotFound(APIResponse<string>.Fail(ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(APIResponse<string>.Fail(ex.Message));
        }
    }

    /// <summary>
    /// Get the dispute already created for this classSession (status, evidence, tutor response
    /// once resolved) — the create endpoint above only returns a one-time creation snapshot.
    /// </summary>
    [HttpGet("class-sessions/{id}/dispute")]
    [Authorize(Roles = UserRole.ParentOrStudent)]
    public async Task<ActionResult<APIResponse<DisputeDetailResponse>>> GetDispute(int id)
    {
        var userId = UserHelper.GetUserId(User);
        var role = User.FindFirstValue(ClaimTypes.Role) ?? "";
        var result = await _disputeService.GetDisputeByClassSessionForUserAsync(id, userId, role);

        if (result == null)
            return NotFound(APIResponse<DisputeDetailResponse>.Fail("Buổi học này không có tranh chấp."));

        return Ok(APIResponse<DisputeDetailResponse>.Success(result, "Lấy thông tin tranh chấp thành công."));
    }

    /// <summary>
    /// Get the parent/student's private chat thread with admin for this classSession's dispute.
    /// </summary>
    [HttpGet("class-sessions/{id}/dispute/thread")]
    [Authorize(Roles = UserRole.ParentOrStudent)]
    public async Task<ActionResult<APIResponse<List<DisputeMessageResponse>>>> GetDisputeThread(int id)
    {
        var userId = UserHelper.GetUserId(User);
        var role = User.FindFirstValue(ClaimTypes.Role) ?? "";
        var result = await _disputeService.GetPartyDisputeThreadAsync(id, userId, role);
        return Ok(APIResponse<List<DisputeMessageResponse>>.Success(result, "Lấy tin nhắn thành công."));
    }

    /// <summary>
    /// Send a message in the parent/student's private chat thread with admin for this classSession's dispute.
    /// </summary>
    [HttpPost("class-sessions/{id}/dispute/thread/messages")]
    [Authorize(Roles = UserRole.ParentOrStudent)]
    public async Task<ActionResult<APIResponse<DisputeMessageResponse>>> SendDisputeThreadMessage(int id, [FromBody] SendDisputeMessageRequest request)
    {
        var userId = UserHelper.GetUserId(User);
        var role = User.FindFirstValue(ClaimTypes.Role) ?? "";
        try
        {
            var result = await _disputeService.SendPartyDisputeMessageAsync(id, userId, role, request.Message);
            return Ok(APIResponse<DisputeMessageResponse>.Success(result, "Gửi tin nhắn thành công."));
        }
        catch (ArgumentException ex)
        {
            return NotFound(APIResponse<DisputeMessageResponse>.Fail(ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(APIResponse<DisputeMessageResponse>.Fail(ex.Message));
        }
    }

    /// <summary>
    /// Get parent's/student's dispute history
    /// </summary>
    [HttpGet("disputes")]
    [Authorize(Roles = UserRole.ParentOrStudent)]
    public async Task<ActionResult<APIResponse<DisputeListPageResponse>>> GetDisputes(
        [FromQuery] PortalDisputeQueryRequest query)
    {
        var userId = UserHelper.GetUserId(User);
        try
        {
            var result = await _parentService.GetParentDisputesAsync(userId, query);
            var payload = new DisputeListPageResponse
            {
                Items = result.ToList(),
                TotalCount = result.TotalCount,
                Page = result.CurrentPage,
                PageSize = result.PageSize
            };
            return Ok(APIResponse<DisputeListPageResponse>.Success(payload, "Lấy danh sách khiếu nại thành công."));
        }
        catch (Exception ex)
        {
            return StatusCode(500, APIResponse<DisputeListPageResponse>.Fail($"Lỗi hệ thống: {ex.Message}", 500));
        }
    }

    /// <summary>
    /// Get parent/student calendar view
    /// </summary>
    [HttpGet("class-sessions/calendar")]
    [Authorize(Roles = UserRole.ParentOrStudent)]
    public async Task<ActionResult<APIResponse<List<CalendarDayResponse>>>> GetCalendar(
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate)
    {
        var userId = UserHelper.GetUserId(User);
        var role = User.FindFirstValue(ClaimTypes.Role) ?? "";
        var start = startDate ?? MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow.Date;
        var end = endDate ?? start.AddDays(30);

        var result = await _parentService.GetParentCalendarAsync(userId, role, start, end);
        return Ok(APIResponse<List<CalendarDayResponse>>.Success(result, "Lấy lịch học thành công."));
    }

    /// <summary>
    /// Get one child's classSessions as a flat list.
    /// </summary>
    [HttpGet("students/{studentId}/class-sessions")]
    [Authorize(Roles = UserRole.ParentOrStudent)]
    public async Task<ActionResult<APIResponse<List<ParentChildClassSessionResponse>>>> GetChildClassSessions(
        string studentId,
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate)
    {
        var userId = UserHelper.GetUserId(User);
        var role = User.FindFirstValue(ClaimTypes.Role) ?? "";

        try
        {
            var result = await _parentService.GetChildClassSessionsAsync(userId, role, studentId, startDate, endDate);
            return Ok(APIResponse<List<ParentChildClassSessionResponse>>.Success(result, "Lấy buổi học của học sinh thành công."));
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, APIResponse<object>.Fail(ex.Message));
        }
    }

    /// <summary>
    /// Buổi học sắp tới gần nhất.
    /// </summary>
    [HttpGet("class-sessions/next")]
    [Authorize(Roles = UserRole.ParentOrStudent)]
    public async Task<ActionResult<APIResponse<ParentChildClassSessionResponse?>>> GetNextClassSession(
        [FromQuery] string? studentId)
    {
        var userId = UserHelper.GetUserId(User);
        var role = User.FindFirstValue(ClaimTypes.Role) ?? "";

        try
        {
            var result = await _parentService.GetNextClassSessionAsync(userId, role, studentId);
            return Ok(APIResponse<ParentChildClassSessionResponse?>.Success(result, "Lấy buổi học sắp tới thành công."));
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, APIResponse<object>.Fail(ex.Message));
        }
    }

    /// <summary>
    /// Lớp học (booking) của MỘT người con. Khác `/parent/bookings` (gộp mọi con và
    /// không cho lọc) — app mobile cần theo con đang chọn.
    /// </summary>
    [HttpGet("students/{studentId}/bookings")]
    [Authorize(Roles = UserRole.ParentOrStudent)]
    public async Task<IActionResult> GetChildBookings(
        string studentId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null,
        [FromQuery] bool excludeClosed = false)
    {
        var userId = UserHelper.GetUserId(User);
        var role = User.FindFirstValue(ClaimTypes.Role) ?? "";

        try
        {
            var result = await _bookingService.GetChildBookingsAsync(userId, role, studentId, page, pageSize, status, excludeClosed);
            var payload = new
            {
                items = (List<BookingResponse>)result,
                totalCount = result.TotalCount,
                currentPage = result.CurrentPage,
                totalPages = result.TotalPages,
                pageSize = result.PageSize
            };
            return Ok(APIResponse<object>.Success(payload, "Lấy lớp học của học sinh thành công."));
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, APIResponse<object>.Fail(ex.Message));
        }
    }

    /// <summary>
    /// Số liệu tổng quan cho Home của phụ huynh.
    /// </summary>
    [HttpGet("home-stats")]
    [Authorize(Roles = UserRole.ParentOrStudent)]
    public async Task<ActionResult<APIResponse<ParentHomeStatsResponse>>> GetHomeStats(
        [FromQuery] string? studentId)
    {
        var userId = UserHelper.GetUserId(User);
        var role = User.FindFirstValue(ClaimTypes.Role) ?? "";

        try
        {
            var result = await _parentService.GetHomeStatsAsync(userId, role, studentId);
            return Ok(APIResponse<ParentHomeStatsResponse>.Success(result, "Lấy số liệu tổng quan thành công."));
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, APIResponse<object>.Fail(ex.Message));
        }
    }

    // =====================================================================
    // STUDENT MANAGEMENT
    // =====================================================================

    /// <summary>
    /// Get all students linked to the parent.
    /// </summary>
    [HttpGet("students")]
    [Authorize(Roles = UserRole.Parent)]
    public async Task<IActionResult> GetStudents()
    {
        var result = await _studentService.GetStudentsByParentIdAsync(GetParentId());
        return Ok(APIResponse<List<StudentProfileResponse>>.Success(result, "Lấy danh sách học sinh thành công."));
    }

    /// <summary>
    /// Create a new student account under this parent.
    /// </summary>
    [HttpPost("students")]
    [Authorize(Roles = UserRole.Parent)]
    public async Task<IActionResult> CreateStudent([FromBody] CreateStudentRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(APIResponse<object>.Fail(ApiMessages.InvalidInputData, 400));

        try
        {
            var result = await _studentService.CreateStudentAsync(request, GetParentId());
            return CreatedAtAction(nameof(GetStudent), new { id = result.StudentId },
                APIResponse<StudentCredentialsResponse>.Success(result, "Tạo học sinh thành công. Vui lòng lưu lại thông tin đăng nhập.", 201));
        }
        catch (InvalidBirthdateException ex)
        {
            return BadRequest(APIResponse<object>.Fail(ex.Message, 400));
        }
        catch (MaxStudentsReachedException)
        {
            return BadRequest(APIResponse<object>.Fail("Đã đạt giới hạn tối đa 5 học sinh cho mỗi phụ huynh.", 400));
        }
    }

    /// <summary>
    /// Get a single student profile — ownership-checked.
    /// </summary>
    [HttpGet("students/{id}")]
    [Authorize(Roles = UserRole.Parent)]
    public async Task<IActionResult> GetStudent(string id)
    {
        try
        {
            var result = await _studentService.GetStudentByIdAsync(id, GetParentId());
            return Ok(APIResponse<StudentProfileResponse>.Success(result, "Lấy thông tin học sinh thành công."));
        }
        catch (StudentNotFoundException)
        {
            return NotFound(APIResponse<object>.Fail("Không tìm thấy học sinh.", 404));
        }
        catch (NotStudentOwnerException)
        {
            return StatusCode(403, APIResponse<object>.Fail(ApiMessages.NotStudentOwner));
        }
    }

    /// <summary>
    /// Update a student's profile fields.
    /// </summary>
    [HttpPut("students/{id}")]
    [Authorize(Roles = UserRole.Parent)]
    public async Task<IActionResult> UpdateStudent(string id, [FromBody] UpdateStudentRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(APIResponse<object>.Fail(ApiMessages.InvalidInputData, 400));

        try
        {
            var result = await _studentService.UpdateStudentAsync(id, request, GetParentId());
            return Ok(APIResponse<StudentProfileResponse>.Success(result, "Cập nhật thông tin học sinh thành công."));
        }
        catch (InvalidBirthdateException ex)
        {
            return BadRequest(APIResponse<object>.Fail("Dữ liệu không hợp lệ: " + ex.Message, 400));
        }
        catch (NotStudentOwnerException)
        {
            return StatusCode(403, APIResponse<object>.Fail(ApiMessages.NotStudentOwner));
        }
    }

    /// <summary>
    /// Delete a student account.
    /// </summary>
    [HttpDelete("students/{id}")]
    [Authorize(Roles = UserRole.Parent)]
    public async Task<IActionResult> DeleteStudent(string id)
    {
        try
        {
            await _studentService.DeleteStudentAsync(id, GetParentId());
            return Ok(APIResponse<object>.Success(new object(), "Xóa học sinh thành công."));
        }
        catch (NotStudentOwnerException)
        {
            return StatusCode(403, APIResponse<object>.Fail(ApiMessages.NotStudentOwner));
        }
        catch (StudentHasActiveBookingException)
        {
            return Conflict(APIResponse<object>.Fail("Không thể xóa học sinh đang có lịch học."));
        }
    }


    /// <summary>
    /// Reset a student's password — parent only.
    /// </summary>
    [HttpPut("students/{id}/reset-password")]
    [Authorize(Roles = UserRole.Parent)]
    public async Task<IActionResult> ResetStudentPassword(string id)
    {
        try
        {
            var result = await _studentService.ResetStudentPasswordAsync(id, GetParentId());
            return Ok(APIResponse<StudentCredentialsResponse>.Success(result, "Đặt lại mật khẩu học sinh thành công. Vui lòng lưu lại thông tin đăng nhập mới."));
        }
        catch (NotStudentOwnerException)
        {
            return StatusCode(403, APIResponse<object>.Fail(ApiMessages.NotStudentOwner));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(APIResponse<object>.Fail(ex.Message, 400));
        }
    }
}
