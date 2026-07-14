using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.PresentationLayer.Helpers;

namespace MV.PresentationLayer.Controllers;

/// <summary>
/// Controller for tutor classSession management - M3
/// </summary>
[ApiController]
[Route("api/tutor/class-sessions")]
[Authorize]
public class TutorClassSessionController : ControllerBase
{
    private readonly IClassSessionService _classSessionService;

    public TutorClassSessionController(IClassSessionService classSessionService)
    {
        _classSessionService = classSessionService;
    }

    /// <summary>
    /// Get tutor classSessions list with filters
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<APIResponse<PagedList<ClassSessionResponse>>>> GetClassSessions(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] string? status = null)
    {
        var tutorId = UserHelper.GetUserId(User);
        var result = await _classSessionService.GetTutorClassSessionsAsync(tutorId, page, pageSize, fromDate, status);
        return Ok(APIResponse<PagedList<ClassSessionResponse>>.Success(result, "Lấy danh sách buổi học thành công."));
    }

    /// <summary>
    /// Get tutor "classes" list — one row per booking, grouped/aggregated server-side
    /// (subject, schedule, progress, derived status). Powers the "Quản lý lớp học" screen.
    /// </summary>
    [HttpGet("/api/tutor/classes")]
    public async Task<ActionResult<APIResponse<TutorClassListResponse>>> GetClasses(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? status = null,
        [FromQuery] string? search = null)
    {
        var tutorId = UserHelper.GetUserId(User);
        var result = await _classSessionService.GetTutorClassesAsync(tutorId, page, pageSize, status, search);
        return Ok(APIResponse<TutorClassListResponse>.Success(result, "Lấy danh sách lớp học thành công."));
    }

    /// <summary>
    /// Get tutor calendar view
    /// </summary>
    [HttpGet("calendar")]
    public async Task<ActionResult<APIResponse<List<CalendarDayResponse>>>> GetCalendar(
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate)
    {
        var tutorId = UserHelper.GetUserId(User);
        var start = startDate ?? MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow.Date;
        var end = endDate ?? start.AddDays(30);

        var result = await _classSessionService.GetTutorCalendarAsync(tutorId, start, end);
        return Ok(APIResponse<List<CalendarDayResponse>>.Success(result, "Lấy lịch học thành công."));
    }

    /// <summary>
    /// Get tutor dashboard statistics
    /// </summary>
    [HttpGet("dashboard")]
    public async Task<ActionResult<APIResponse<TutorDashboardStatsResponse>>> GetDashboard()
    {
        var tutorId = UserHelper.GetUserId(User);
        var result = await _classSessionService.GetTutorDashboardStatsAsync(tutorId);
        return Ok(APIResponse<TutorDashboardStatsResponse>.Success(result, "Lấy thống kê bảng điều khiển thành công."));
    }

    /// <summary>
    /// Get classSession detail
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<APIResponse<ClassSessionDetailResponse>>> GetClassSessionDetail(int id)
    {
        var tutorId = UserHelper.GetUserId(User);
        var result = await _classSessionService.GetTutorClassSessionDetailAsync(id, tutorId);

        if (result == null)
            return NotFound(APIResponse<ClassSessionDetailResponse>.Fail(ApiMessages.ClassSessionNotFound));

        return Ok(APIResponse<ClassSessionDetailResponse>.Success(result, "Lấy chi tiết buổi học thành công."));
    }

    /// <summary>
    /// Check-in to a classSession (available ±15 min from scheduled start)
    /// </summary>
    [HttpPut("{classSessionId}/checkin")]
    public async Task<ActionResult<APIResponse<ClassSessionDetailResponse>>> CheckIn(int classSessionId, [FromBody] CheckInRequest request)
    {
        var tutorId = UserHelper.GetUserId(User);
        var result = await _classSessionService.CheckInAsync(classSessionId, tutorId, request);
        return Ok(APIResponse<ClassSessionDetailResponse>.Success(result, "Điểm danh vào buổi học thành công."));
    }

    /// <summary>
    /// Check-out from a classSession
    /// </summary>
    [HttpPut("{classSessionId}/checkout")]
    public async Task<ActionResult<APIResponse<ClassSessionDetailResponse>>> CheckOut(int classSessionId, [FromBody] CheckOutRequest request)
    {
        var tutorId = UserHelper.GetUserId(User);
        var result = await _classSessionService.CheckOutAsync(classSessionId, tutorId, request);
        return Ok(APIResponse<ClassSessionDetailResponse>.Success(result, "Điểm danh ra buổi học thành công."));
    }

    /// <summary>
    /// Submit classSession report
    /// </summary>
    [HttpPut("{id}/report")]
    public async Task<ActionResult<APIResponse<ClassSessionDetailResponse>>> SubmitReport(int id, [FromBody] SubmitReportRequest request)
    {
        var tutorId = UserHelper.GetUserId(User);
        var result = await _classSessionService.SubmitReportAsync(id, tutorId, request);
        return Ok(APIResponse<ClassSessionDetailResponse>.Success(result, "Gửi báo cáo buổi học thành công."));
    }

    /// <summary>
    /// Upload attachment for a classSession
    /// </summary>
    [HttpPost("{id}/attachments")]
    public async Task<ActionResult<APIResponse<string>>> UploadAttachment(int id, IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(APIResponse<string>.Fail("Tệp đính kèm là bắt buộc."));

        var tutorId = UserHelper.GetUserId(User);
        var result = await _classSessionService.UploadAttachmentAsync(id, tutorId, file);
        return Ok(APIResponse<string>.Success(result, "Tải tệp đính kèm thành công."));
    }
}
