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
    private readonly IDisputeService _disputeService;

    public TutorClassSessionController(IClassSessionService classSessionService, IDisputeService disputeService)
    {
        _classSessionService = classSessionService;
        _disputeService = disputeService;
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
    /// Check-out (kết thúc buổi học). Check-in nay là tự động khi cả gia sư và học viên cùng
    /// vào phòng (xem AgoraController heartbeat) — không còn endpoint check-in thủ công.
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

    /// <summary>
    /// Get the dispute tied to this classSession, if any (tutor's own rebuttal view).
    /// </summary>
    [HttpGet("{id}/dispute")]
    public async Task<ActionResult<APIResponse<DisputeDetailResponse>>> GetDispute(int id)
    {
        var tutorId = UserHelper.GetUserId(User);
        var result = await _disputeService.GetTutorDisputeByClassSessionAsync(id, tutorId);

        if (result == null)
            return NotFound(APIResponse<DisputeDetailResponse>.Fail("Buổi học này không có tranh chấp."));

        return Ok(APIResponse<DisputeDetailResponse>.Success(result, "Lấy thông tin tranh chấp thành công."));
    }

    /// <summary>
    /// Submit a written rebuttal to a dispute raised against the tutor for this classSession.
    /// </summary>
    [HttpPost("{id}/dispute/response")]
    public async Task<ActionResult<APIResponse<DisputeDetailResponse>>> SubmitDisputeResponse(int id, [FromBody] SubmitTutorResponseRequest request)
    {
        var tutorId = UserHelper.GetUserId(User);
        try
        {
            var result = await _disputeService.SubmitTutorResponseAsync(id, tutorId, request.Response);
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
    /// Upload supporting evidence for a dispute raised against the tutor for this classSession.
    /// </summary>
    [HttpPost("{id}/dispute/evidence")]
    public async Task<ActionResult<APIResponse<string>>> UploadDisputeEvidence(int id, IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(APIResponse<string>.Fail("Tệp bằng chứng là bắt buộc."));

        var tutorId = UserHelper.GetUserId(User);
        try
        {
            var result = await _disputeService.UploadTutorDisputeEvidenceAsync(id, tutorId, file);
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
    /// Get all disputes across the tutor's own classSessions with filtering, sorting, and pagination.
    /// </summary>
    [HttpGet("/api/tutor/disputes")]
    public async Task<ActionResult<APIResponse<DisputeListPageResponse>>> GetDisputes(
        [FromQuery] PortalDisputeQueryRequest query)
    {
        var tutorId = UserHelper.GetUserId(User);
        var result = await _disputeService.GetTutorDisputesAsync(tutorId, query);
        var payload = new DisputeListPageResponse
        {
            Items = result.ToList(),
            TotalCount = result.TotalCount,
            Page = result.CurrentPage,
            PageSize = result.PageSize
        };

        return Ok(APIResponse<DisputeListPageResponse>.Success(payload, "Lấy danh sách tranh chấp thành công."));
    }

    /// <summary>
    /// Get the tutor's private chat thread with admin for this classSession's dispute.
    /// </summary>
    [HttpGet("{id}/dispute/thread")]
    public async Task<ActionResult<APIResponse<List<DisputeMessageResponse>>>> GetDisputeThread(int id)
    {
        var tutorId = UserHelper.GetUserId(User);
        var result = await _disputeService.GetTutorDisputeThreadAsync(id, tutorId);
        return Ok(APIResponse<List<DisputeMessageResponse>>.Success(result, "Lấy tin nhắn thành công."));
    }

    /// <summary>
    /// Send a message in the tutor's private chat thread with admin for this classSession's dispute.
    /// </summary>
    [HttpPost("{id}/dispute/thread/messages")]
    public async Task<ActionResult<APIResponse<DisputeMessageResponse>>> SendDisputeThreadMessage(int id, [FromBody] SendDisputeMessageRequest request)
    {
        var tutorId = UserHelper.GetUserId(User);
        try
        {
            var result = await _disputeService.SendTutorDisputeMessageAsync(id, tutorId, request.Message);
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
}
