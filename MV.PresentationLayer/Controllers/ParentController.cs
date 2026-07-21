using MV.DomainLayer.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
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
    private readonly ILogger<ParentController> _logger;

    public ParentController(
        IParentService parentService,
        IStudentService studentService,
        IClassSessionService classSessionService,
        IDisputeService disputeService,
        ILogger<ParentController> logger)
    {
        _parentService = parentService;
        _studentService = studentService;
        _classSessionService = classSessionService;
        _disputeService = disputeService;
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
    [Authorize(Roles = UserRole.ParentOrStudent)]
    public async Task<ActionResult<APIResponse<DisputeDetailResponse>>> CreateDispute(int id, [FromBody] CreateDisputeRequest request)
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
    /// Upload dispute evidence for a classSession (images/PDF).
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
            var fileUrl = await _parentService.UploadDisputeEvidenceAsync(id, userId, role, file);
            return Ok(APIResponse<string>.Success(fileUrl, "Tải tệp bằng chứng thành công."));
        }
        catch (ClassSessionException ex)
        {
            return StatusCode(ex.HttpStatus, APIResponse<object>.Fail(ex.Message, ex.HttpStatus));
        }
        catch (Exception ex)
        {
            return StatusCode(500, APIResponse<object>.Fail($"Lỗi hệ thống: {ex.Message}", 500));
        }
    }

    /// <summary>
    /// Get parent's/student's dispute history
    /// </summary>
    [HttpGet("disputes")]
    [Authorize(Roles = UserRole.ParentOrStudent)]
    public async Task<ActionResult<APIResponse<PagedList<DisputeListResponse>>>> GetDisputes(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10)
    {
        var userId = UserHelper.GetUserId(User);
        var role = User.FindFirstValue(ClaimTypes.Role) ?? "";
        try
        {
            var result = await _parentService.GetParentDisputesAsync(userId, role, page, pageSize);
            return Ok(APIResponse<PagedList<DisputeListResponse>>.Success(result, "Lấy danh sách khiếu nại thành công."));
        }
        catch (Exception ex)
        {
            return StatusCode(500, APIResponse<PagedList<DisputeListResponse>>.Fail($"Lỗi hệ thống: {ex.Message}", 500));
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
