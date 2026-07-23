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
    public async Task<ActionResult<APIResponse<PagedList<DisputeListResponse>>>> GetDisputes([FromQuery] DisputeQueryRequest query)
    {
        var result = await _disputeService.GetDisputesAsync(query);
        return Ok(APIResponse<PagedList<DisputeListResponse>>.Success(result, "Lấy danh sách tranh chấp thành công."));
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
    public async Task<ActionResult<APIResponse<List<ChatMessageResponse>>>> GetChatHistory(int id)
    {
        var result = await _disputeService.GetDisputeChatHistoryAsync(id);
        return Ok(APIResponse<List<ChatMessageResponse>>.Success(result, "Lấy lịch sử chat thành công."));
    }

    /// <summary>
    /// Start investigating a dispute
    /// </summary>
    [RequirePermission(Permissions.DisputeInvestigate)]
    [HttpPut("{id}/investigate")]
    public async Task<ActionResult<APIResponse<DisputeDetailResponse>>> Investigate(int id)
    {
        var adminId = UserHelper.GetUserId(User);
        var result = await _disputeService.InvestigateDisputeAsync(id, adminId);
        return Ok(APIResponse<DisputeDetailResponse>.Success(result, "Đã bắt đầu điều tra tranh chấp."));
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
}
