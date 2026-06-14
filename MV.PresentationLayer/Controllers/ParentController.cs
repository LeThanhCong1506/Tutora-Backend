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
/// Controller for parent — lesson management and student management
/// </summary>
[ApiController]
[Route("api/parent")]
[Authorize]
public class ParentController : ControllerBase
{
    private readonly IParentService _parentService;
    private readonly IStudentService _studentService;
    private readonly ILessonService _lessonService;
    private readonly ILogger<ParentController> _logger;

    public ParentController(
        IParentService parentService,
        IStudentService studentService,
        ILessonService lessonService,
        ILogger<ParentController> logger)
    {
        _parentService = parentService;
        _studentService = studentService;
        _lessonService = lessonService;
        _logger = logger;
    }

    private string GetParentId() => User.FindFirstValue(ClaimTypes.NameIdentifier)!;


    // =====================================================================
    // LESSON MANAGEMENT
    // =====================================================================

    /// <summary>
    /// Get lessons pending confirmation
    /// </summary>
    [HttpGet("lessons/pending")]
    [Authorize(Roles = UserRole.ParentOrStudent)]
    public async Task<ActionResult<APIResponse<List<PendingLessonResponse>>>> GetPendingLessons()
    {
        try
        {
            var userId = UserHelper.GetUserId(User);
            var role = User.FindFirstValue(ClaimTypes.Role) ?? "";
            _logger.LogInformation("GetPendingLessons called for userId: {UserId}, role: {Role}", userId, role);
            var result = await _parentService.GetPendingLessonsAsync(userId, role);
            return Ok(APIResponse<List<PendingLessonResponse>>.Success(result, "Lấy danh sách buổi học chờ xác nhận thành công."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetPendingLessons FAILED: {Message}", ex.Message);
            return StatusCode(500, APIResponse<List<PendingLessonResponse>>.Fail($"Lỗi hệ thống: {ex.Message}"));
        }
    }

    /// <summary>
    /// Get lesson detail
    /// </summary>
    [HttpGet("lessons/{id}")]
    [Authorize(Roles = UserRole.ParentOrStudent)]
    public async Task<ActionResult<APIResponse<LessonDetailResponse>>> GetLessonDetail(int id)
    {
        var userId = UserHelper.GetUserId(User);
        var role = User.FindFirstValue(ClaimTypes.Role) ?? "";
        var result = await _parentService.GetLessonDetailAsync(id, userId, role);

        if (result == null)
            return NotFound(APIResponse<LessonDetailResponse>.Fail(ApiMessages.LessonNotFound));

        return Ok(APIResponse<LessonDetailResponse>.Success(result, "Lấy chi tiết buổi học thành công."));
    }

    /// <summary>
    /// Confirm a lesson as completed
    /// </summary>
    [HttpPut("lessons/{id}/confirm")]
    [Authorize(Roles = UserRole.ParentOrStudent)]
    public async Task<ActionResult<APIResponse<SettlementResultResponse>>> ConfirmLesson(int id)
    {
        var userId = UserHelper.GetUserId(User);
        var role = User.FindFirstValue(ClaimTypes.Role) ?? "";
        var result = await _parentService.ConfirmLessonAsync(id, userId, role);
        return Ok(APIResponse<SettlementResultResponse>.Success(result, "Xác nhận buổi học thành công."));
    }

    /// <summary>
    /// Get parent/student calendar view
    /// </summary>
    [HttpGet("lessons/calendar")]
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
    /// Generate a link code for a student to scan/enter.
    /// </summary>
    [HttpPost("students/{id}/generate-link-code")]
    [Authorize(Roles = UserRole.Parent)]
    public async Task<IActionResult> GenerateLinkCode(string id)
    {
        try
        {
            var result = await _studentService.GenerateLinkCodeAsync(id, GetParentId());
            return Ok(APIResponse<StudentProfileResponse>.Success(result, "Tạo mã liên kết thành công."));
        }
        catch (NotStudentOwnerException)
        {
            return StatusCode(403, APIResponse<object>.Fail(ApiMessages.NotStudentOwner));
        }
    }

    /// <summary>
    /// Student links their account using a code generated by the parent.
    /// </summary>
    [HttpPost("students/link")]
    [Authorize(Roles = UserRole.Student)]
    public async Task<IActionResult> LinkStudentWithCode([FromBody] LinkStudentRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(APIResponse<object>.Fail(ApiMessages.InvalidInputData, 400));

        var studentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(studentUserId))
            return Unauthorized(APIResponse<object>.Fail(ApiMessages.UserNotAuthenticated, 401));

        try
        {
            var result = await _studentService.LinkStudentWithCodeAsync(request.Code, studentUserId);
            return Ok(APIResponse<StudentProfileResponse>.Success(result, "Liên kết tài khoản học sinh thành công."));
        }
        catch (LinkCodeNotFoundException)
        {
            return NotFound(APIResponse<object>.Fail("Mã liên kết không hợp lệ.", 404));
        }
        catch (LinkCodeExpiredException)
        {
            return BadRequest(APIResponse<object>.Fail("Mã liên kết đã hết hạn.", 400));
        }
        catch (StudentAlreadyLinkedException)
        {
            return Conflict(APIResponse<object>.Fail("Học sinh này đã được liên kết với tài khoản khác.", 409));
        }
    }

    /// <summary>
    /// Parent generates a code that a student can use to link themselves.
    /// </summary>
    [HttpPost("students/generate-parent-code")]
    [Authorize(Roles = UserRole.Parent)]
    public async Task<IActionResult> GenerateParentCode()
    {
        try
        {
            var code = await _studentService.GenerateParentCodeAsync(GetParentId());
            return Ok(APIResponse<object>.Success(new { ParentCode = code }, "Tạo mã phụ huynh thành công. Mã có hiệu lực trong 24 giờ."));
        }
        catch (Exception ex)
        {
            return BadRequest(APIResponse<object>.Fail(ex.Message, 400));
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
