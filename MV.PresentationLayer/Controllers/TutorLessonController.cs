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
/// Controller for tutor lesson management - M3
/// </summary>
[ApiController]
[Route("api/tutor/lessons")]
[Authorize]
public class TutorLessonController : ControllerBase
{
    private readonly ILessonService _lessonService;

    public TutorLessonController(ILessonService lessonService)
    {
        _lessonService = lessonService;
    }

    /// <summary>
    /// Get tutor lessons list with filters
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<APIResponse<PagedList<LessonResponse>>>> GetLessons(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] string? status = null)
    {
        var tutorId = UserHelper.GetUserId(User);
        var result = await _lessonService.GetTutorLessonsAsync(tutorId, page, pageSize, fromDate, status);
        return Ok(APIResponse<PagedList<LessonResponse>>.Success(result, "Lấy danh sách buổi học thành công."));
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
        var start = startDate ?? MV.DomainLayer.Helpers.VietnamTimeHelper.Now.Date;
        var end = endDate ?? start.AddDays(30);

        var result = await _lessonService.GetTutorCalendarAsync(tutorId, start, end);
        return Ok(APIResponse<List<CalendarDayResponse>>.Success(result, "Lấy lịch học thành công."));
    }

    /// <summary>
    /// Get tutor dashboard statistics
    /// </summary>
    [HttpGet("dashboard")]
    public async Task<ActionResult<APIResponse<TutorDashboardStatsResponse>>> GetDashboard()
    {
        var tutorId = UserHelper.GetUserId(User);
        var result = await _lessonService.GetTutorDashboardStatsAsync(tutorId);
        return Ok(APIResponse<TutorDashboardStatsResponse>.Success(result, "Lấy thống kê bảng điều khiển thành công."));
    }

    /// <summary>
    /// Get lesson detail
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<APIResponse<LessonDetailResponse>>> GetLessonDetail(int id)
    {
        var tutorId = UserHelper.GetUserId(User);
        var result = await _lessonService.GetTutorLessonDetailAsync(id, tutorId);

        if (result == null)
            return NotFound(APIResponse<LessonDetailResponse>.Fail(ApiMessages.LessonNotFound));

        return Ok(APIResponse<LessonDetailResponse>.Success(result, "Lấy chi tiết buổi học thành công."));
    }

    /// <summary>
    /// Check-in to a lesson (available ±15 min from scheduled start)
    /// </summary>
    [HttpPut("{lessonId}/checkin")]
    public async Task<ActionResult<APIResponse<LessonDetailResponse>>> CheckIn(int lessonId, [FromBody] CheckInRequest request)
    {
        var tutorId = UserHelper.GetUserId(User);
        var result = await _lessonService.CheckInAsync(lessonId, tutorId, request);
        return Ok(APIResponse<LessonDetailResponse>.Success(result, "Điểm danh vào buổi học thành công."));
    }

    /// <summary>
    /// Check-out from a lesson
    /// </summary>
    [HttpPut("{lessonId}/checkout")]
    public async Task<ActionResult<APIResponse<LessonDetailResponse>>> CheckOut(int lessonId, [FromBody] CheckOutRequest request)
    {
        var tutorId = UserHelper.GetUserId(User);
        var result = await _lessonService.CheckOutAsync(lessonId, tutorId, request);
        return Ok(APIResponse<LessonDetailResponse>.Success(result, "Điểm danh ra buổi học thành công."));
    }

    /// <summary>
    /// Submit lesson report
    /// </summary>
    [HttpPut("{id}/report")]
    public async Task<ActionResult<APIResponse<LessonDetailResponse>>> SubmitReport(int id, [FromBody] SubmitReportRequest request)
    {
        var tutorId = UserHelper.GetUserId(User);
        var result = await _lessonService.SubmitReportAsync(id, tutorId, request);
        return Ok(APIResponse<LessonDetailResponse>.Success(result, "Gửi báo cáo buổi học thành công."));
    }

    /// <summary>
    /// Upload attachment for a lesson
    /// </summary>
    [HttpPost("{id}/attachments")]
    public async Task<ActionResult<APIResponse<string>>> UploadAttachment(int id, IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(APIResponse<string>.Fail("Tệp đính kèm là bắt buộc."));

        var tutorId = UserHelper.GetUserId(User);
        var result = await _lessonService.UploadAttachmentAsync(id, tutorId, file);
        return Ok(APIResponse<string>.Success(result, "Tải tệp đính kèm thành công."));
    }
}
