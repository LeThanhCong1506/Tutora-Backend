using MV.DomainLayer.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.DTO;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.PresentationLayer.Helpers;
using MV.PresentationLayer.Authorization;

namespace MV.PresentationLayer.Controllers;

/// <summary>
/// Controller for admin dispute management - M3
/// </summary>
[ApiController]
[Route("api/admin/disputes")]
[Authorize]
public class DisputeController : ControllerBase
{
    private readonly IDisputeService _disputeService;

    public DisputeController(IDisputeService disputeService)
    {
        _disputeService = disputeService;
    }

    /// <summary>
    /// Get list of disputes with filters
    /// </summary>
    [RequirePermission(Permissions.DisputeView)]
    [HttpGet]
    public async Task<ActionResult<APIResponse<DisputeListPageResponse>>> GetDisputes([FromQuery] DisputeQueryRequest query)
    {
        var result = await _disputeService.GetDisputesAsync(query);
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
    /// Get dispute statistics
    /// </summary>
    [RequirePermission(Permissions.DisputeView)]
    [HttpGet("stats")]
    public async Task<ActionResult<APIResponse<DisputeStatsResponse>>> GetStats()
    {
        var result = await _disputeService.GetDisputeStatsAsync();
        return Ok(APIResponse<DisputeStatsResponse>.Success(result, "Lấy thống kê thành công."));
    }

    /// <summary>
    /// Get dispute detail
    /// </summary>
    [RequirePermission(Permissions.DisputeView)]
    [HttpGet("{id}")]
    public async Task<ActionResult<APIResponse<DisputeDetailResponse>>> GetDisputeDetail(int id)
    {
        var actorId = UserHelper.GetUserId(User);
        var result = await _disputeService.GetDisputeDetailAsync(id, actorId);

        if (result == null)
            return NotFound(APIResponse<DisputeDetailResponse>.Fail("Không tìm thấy tranh chấp."));

        return Ok(APIResponse<DisputeDetailResponse>.Success(result, "Lấy chi tiết tranh chấp thành công."));
    }

    /// <summary>
    /// Lấy bản ghi video (trạng thái + link stream tạm) của buổi học gắn với tranh chấp — phục vụ xử lý tranh chấp.
    /// </summary>
    [RequirePermission(Permissions.DisputeView)]
    [HttpGet("{id}/recording")]
    public async Task<ActionResult<APIResponse<DisputeRecordingResponse>>> GetRecording(int id)
    {
        var actorId = UserHelper.GetUserId(User);
        var result = await _disputeService.GetDisputeRecordingAsync(id, actorId);
        return Ok(APIResponse<DisputeRecordingResponse>.Success(result, "Lấy video buổi học thành công."));
    }

    /// <summary>
    /// Get chat history for dispute context
    /// </summary>
    [RequirePermission(Permissions.DisputeView)]
    [HttpGet("{id}/chat")]
    public async Task<ActionResult<APIResponse<DisputeChatHistoryResponse>>> GetChatHistory(int id)
    {
        var result = await _disputeService.GetDisputeChatHistoryAsync(id);
        return Ok(APIResponse<DisputeChatHistoryResponse>.Success(result, "Lấy lịch sử chat thành công."));
    }

    /// <summary>
    /// Start investigating a dispute
    /// </summary>
    [RequirePermission(Permissions.DisputeInvestigate)]
    [HttpPut("{id}/investigate")]
    public async Task<ActionResult<APIResponse<DisputeDetailResponse>>> Investigate(int id, [FromQuery] bool forceEarly = false)
    {
        var adminId = UserHelper.GetUserId(User);
        var result = await _disputeService.InvestigateDisputeAsync(id, adminId, forceEarly);
        return Ok(APIResponse<DisputeDetailResponse>.Success(result, "Đã bắt đầu điều tra tranh chấp."));
    }

    /// <summary>
    /// (Re)run AI priority classification for a dispute. Used to backfill disputes created before this
    /// feature existed, or to retry one whose automatic classification failed.
    /// </summary>
    [RequirePermission(Permissions.DisputeInvestigate)]
    [HttpPut("{id}/classify")]
    public async Task<ActionResult<APIResponse<DisputeDetailResponse>>> Classify(int id)
    {
        var adminId = UserHelper.GetUserId(User);
        DisputeDetailResponse? result;
        try
        {
            result = await _disputeService.ClassifyDisputePriorityAsync(id, adminId);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
        {
            return StatusCode(StatusCodes.Status502BadGateway,
                APIResponse<DisputeDetailResponse>.Fail("Không thể phân loại ưu tiên bằng AI. Vui lòng thử lại sau."));
        }

        if (result == null)
            return NotFound(APIResponse<DisputeDetailResponse>.Fail("Không tìm thấy tranh chấp."));

        return Ok(APIResponse<DisputeDetailResponse>.Success(result, "Đã phân loại tranh chấp."));
    }
    /// <summary>
    /// Preview parent refund / tutor payout amounts for a candidate percentage before resolving.
    /// </summary>
    [RequirePermission(Permissions.DisputeResolve)]
    [HttpGet("{id}/refund-preview")]
    public async Task<ActionResult<APIResponse<RefundPreviewResponse>>> GetRefundPreview(int id, [FromQuery] int percentage)
    {
        var result = await _disputeService.GetRefundPreviewAsync(id, percentage);
        return Ok(APIResponse<RefundPreviewResponse>.Success(result, "Tính toán xem trước thành công."));
    }

    /// <summary>
    /// Preview số buổi/số tiền sẽ hủy+hoàn nếu resolve bằng "Hủy khóa học & hoàn tiền" (case 4).
    /// </summary>
    [RequirePermission(Permissions.DisputeResolve)]
    [HttpGet("{id}/cancel-course-preview")]
    public async Task<ActionResult<APIResponse<CourseCancelPreviewResponse>>> GetCancelCoursePreview(int id)
    {
        var result = await _disputeService.GetCancelCoursePreviewAsync(id);
        return Ok(APIResponse<CourseCancelPreviewResponse>.Success(result, "Tính toán xem trước thành công."));
    }

    /// <summary>
    /// Resolve a dispute
    /// </summary>
    [RequirePermission(Permissions.DisputeResolve)]
    [HttpPut("{id}/resolve")]
    public async Task<ActionResult<APIResponse<DisputeDetailResponse>>> Resolve(int id, [FromBody] ResolveDisputeRequest request)
    {
        var adminId = UserHelper.GetUserId(User);
        var result = await _disputeService.ResolveDisputeAsync(id, adminId, request);
        return Ok(APIResponse<DisputeDetailResponse>.Success(result, "Giải quyết tranh chấp thành công."));
    }

    /// <summary>
    /// Đóng tranh chấp khi hai bên đã hoà giải và muốn học tiếp — không phân xử ai đúng ai sai,
    /// không hoàn tiền. Trạng thái buổi học do admin chọn (tính là đã học, hoặc học lại).
    /// </summary>
    [RequirePermission(Permissions.DisputeResolve)]
    [HttpPut("{id}/close")]
    public async Task<ActionResult<APIResponse<DisputeDetailResponse>>> Close(int id, [FromBody] CloseDisputeRequest request)
    {
        var adminId = UserHelper.GetUserId(User);
        try
        {
            var result = await _disputeService.CloseDisputeAsync(id, adminId, request);
            return Ok(APIResponse<DisputeDetailResponse>.Success(result, "Đã đóng phản ánh do hai bên hoà giải."));
        }
        // Không có catch trước đây: mọi lỗi (kể cả các thông báo cụ thể như "đã học lại N lần,
        // không thể học lại tiếp" hay "giờ học lại phải ở tương lai") đều rơi vào
        // ExceptionHandlingMiddleware và bị thay bằng message tiếng Anh chung chung, làm admin
        // không biết lý do thật. Bắt riêng ở đây để trả đúng message như các action tranh chấp khác.
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
    /// Get one of the two private threads (tutor or parent/student) for a dispute.
    /// </summary>
    [RequirePermission(Permissions.DisputeView)]
    [HttpGet("{id}/thread/{threadType}")]
    public async Task<ActionResult<APIResponse<List<DisputeMessageResponse>>>> GetThread(int id, string threadType)
    {
        var result = await _disputeService.GetDisputeThreadAsync(id, threadType);
        return Ok(APIResponse<List<DisputeMessageResponse>>.Success(result, "Lấy tin nhắn thành công."));
    }

    /// <summary>
    /// Send a message into one of the two private threads for a dispute.
    /// </summary>
    [RequirePermission(Permissions.DisputeInvestigate)]
    [HttpPost("{id}/thread/{threadType}/messages")]
    public async Task<ActionResult<APIResponse<DisputeMessageResponse>>> SendThreadMessage(int id, string threadType, [FromBody] SendDisputeMessageRequest request)
    {
        var adminId = UserHelper.GetUserId(User);
        var result = await _disputeService.SendAdminDisputeMessageAsync(id, adminId, threadType, request.Message);
        return Ok(APIResponse<DisputeMessageResponse>.Success(result, "Gửi tin nhắn thành công."));
    }
}
