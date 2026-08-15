using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.PresentationLayer.Authorization;
using MV.PresentationLayer.Helpers;

namespace MV.PresentationLayer.Controllers;

/// <summary>
/// Admin/Staff support inbox — direct messaging with tutor/parent/student, independent of
/// disputes (one continuous thread per user, not scoped to a booking/incident).
/// </summary>
[ApiController]
[Route("api/admin/support")]
[Authorize]
public class AdminSupportController : ControllerBase
{
    private const long MaxImageSizeBytes = 5 * 1024 * 1024;

    private readonly ISupportMessageService _supportService;

    public AdminSupportController(ISupportMessageService supportService)
    {
        _supportService = supportService;
    }

    /// <summary>Conversation list for the support inbox. Optional role filter and name/phone search.</summary>
    [RequirePermission(Permissions.SupportView)]
    [HttpGet("threads")]
    public async Task<ActionResult<APIResponse<List<SupportThreadSummaryResponse>>>> GetThreads(
        [FromQuery] string? role, [FromQuery] string? search)
    {
        var result = await _supportService.GetThreadsForAdminAsync(role, search);
        return Ok(APIResponse<List<SupportThreadSummaryResponse>>.Success(result, "Lấy danh sách hội thoại thành công."));
    }

    /// <summary>Gets (creating if needed) the thread with a user and marks it read for admin.</summary>
    [RequirePermission(Permissions.SupportView)]
    [HttpGet("threads/{userId}")]
    public async Task<ActionResult<APIResponse<SupportThreadDetailResponse>>> GetThread(string userId)
    {
        var result = await _supportService.GetOrCreateThreadForAdminAsync(userId);
        if (result == null)
            return NotFound(APIResponse.Fail("Không tìm thấy người dùng.", 404));

        return Ok(APIResponse<SupportThreadDetailResponse>.Success(result, "Lấy hội thoại thành công."));
    }

    /// <summary>Sends a message from admin/staff to a user, creating the thread on first contact.</summary>
    [RequirePermission(Permissions.SupportView)]
    [HttpPost("threads/{userId}/messages")]
    public async Task<ActionResult<APIResponse<SupportMessageItemResponse>>> SendMessage(
        string userId, [FromBody] SendSupportMessageRequest request)
    {
        var adminId = UserHelper.GetUserId(User);
        var result = await _supportService.SendMessageAsAdminAsync(userId, adminId, request.Message);
        if (result == null)
            return NotFound(APIResponse.Fail("Không tìm thấy người dùng.", 404));

        return Ok(APIResponse<SupportMessageItemResponse>.Success(result, "Gửi tin nhắn thành công."));
    }

    /// <summary>Uploads and sends an image from admin/staff to a user.</summary>
    [RequirePermission(Permissions.SupportView)]
    [HttpPost("threads/{userId}/images")]
    [RequestSizeLimit(MaxImageSizeBytes)]
    public async Task<ActionResult<APIResponse<SupportMessageItemResponse>>> SendImage(string userId, IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(APIResponse.Fail("Không có file được chọn.", 400));
        if (file.Length > MaxImageSizeBytes)
            return BadRequest(APIResponse.Fail("Kích thước file vượt quá 5MB.", 400));
        if (!file.ContentType.StartsWith("image/"))
            return BadRequest(APIResponse.Fail("Chỉ chấp nhận file hình ảnh.", 400));

        var adminId = UserHelper.GetUserId(User);
        var result = await _supportService.SendImageAsAdminAsync(userId, adminId, file);
        if (result == null)
            return NotFound(APIResponse.Fail("Không tìm thấy người dùng.", 404));

        return Ok(APIResponse<SupportMessageItemResponse>.Success(result, "Gửi hình ảnh thành công."));
    }
}
