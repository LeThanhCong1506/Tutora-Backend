using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Exceptions;
using MV.PresentationLayer.Helpers;

namespace MV.PresentationLayer.Controllers;

/// <summary>
/// Học sinh tóm tắt video buổi học bằng Gemini + hỏi tiếp qua chat. Chỉ Student — không mở
/// cho Tutor/Parent (khác cơ chế auto-fill báo cáo của gia sư, xem TutorClassSessionController).
/// </summary>
[ApiController]
[Route("api/student/class-sessions/{id}/video-summary")]
[Authorize(Roles = UserRole.Student)]
// Trạng thái job đổi liên tục (pending → processing → completed) trong lúc FE poll mỗi 8s —
// từng bị 1 tầng cache ngoài ứng dụng (nginx/CDN, không nằm trong repo, thấy qua header
// "X-Cache: HIT" trên production) trả lại response cũ, làm học sinh thấy tóm tắt "biến mất"
// dù đã xong từ lâu. NoStore báo tường minh cho MỌI tầng cache trung gian không được lưu.
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class ClassSessionVideoSummaryController(IClassSessionVideoAiService videoAiService) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<APIResponse<ClassSessionAiJobResponse>>> Trigger(int id)
    {
        var userId = UserHelper.GetUserId(User);
        try
        {
            var result = await videoAiService.TriggerStudentSummaryAsync(id, userId);
            return Ok(APIResponse<ClassSessionAiJobResponse>.Success(result, "Đã gửi yêu cầu tóm tắt video."));
        }
        catch (NotFoundException ex) { return NotFound(APIResponse.Fail(ex.Message, 404)); }
        catch (KeyNotFoundException ex) { return NotFound(APIResponse.Fail(ex.Message, 404)); }
        catch (UnauthorizedAccessException ex) { return StatusCode(403, APIResponse.Fail(ex.Message, 403)); }
        catch (BadRequestException ex) { return BadRequest(APIResponse.Fail(ex.Message, 400)); }
        catch (InvalidOperationException ex) { return BadRequest(APIResponse.Fail(ex.Message, 400)); }
    }

    [HttpGet]
    public async Task<ActionResult<APIResponse<ClassSessionAiJobResponse>>> GetStatus(int id)
    {
        var userId = UserHelper.GetUserId(User);
        try
        {
            var result = await videoAiService.GetStudentSummaryStatusAsync(id, userId);
            return Ok(APIResponse<ClassSessionAiJobResponse>.Success(result, "Lấy trạng thái tóm tắt video thành công."));
        }
        catch (NotFoundException ex) { return NotFound(APIResponse.Fail(ex.Message, 404)); }
        catch (KeyNotFoundException ex) { return NotFound(APIResponse.Fail(ex.Message, 404)); }
        catch (UnauthorizedAccessException ex) { return StatusCode(403, APIResponse.Fail(ex.Message, 403)); }
    }

    [HttpPost("messages")]
    public async Task<ActionResult<APIResponse<string>>> AskFollowUp(int id, [FromBody] AskVideoSummaryFollowUpRequest request)
    {
        var userId = UserHelper.GetUserId(User);
        try
        {
            var answer = await videoAiService.AskFollowUpAsync(id, userId, request.Question);
            return Ok(APIResponse<string>.Success(answer, "Đã trả lời câu hỏi."));
        }
        catch (NotFoundException ex) { return NotFound(APIResponse.Fail(ex.Message, 404)); }
        catch (KeyNotFoundException ex) { return NotFound(APIResponse.Fail(ex.Message, 404)); }
        catch (UnauthorizedAccessException ex) { return StatusCode(403, APIResponse.Fail(ex.Message, 403)); }
        catch (BadRequestException ex) { return BadRequest(APIResponse.Fail(ex.Message, 400)); }
        catch (InvalidOperationException ex) { return BadRequest(APIResponse.Fail(ex.Message, 400)); }
    }

    [HttpGet("messages")]
    public async Task<ActionResult<APIResponse<List<ClassSessionVideoChatMessageResponse>>>> GetMessages(int id)
    {
        var userId = UserHelper.GetUserId(User);
        try
        {
            var result = await videoAiService.GetFollowUpMessagesAsync(id, userId);
            return Ok(APIResponse<List<ClassSessionVideoChatMessageResponse>>.Success(result, "Lấy lịch sử chat thành công."));
        }
        catch (NotFoundException ex) { return NotFound(APIResponse.Fail(ex.Message, 404)); }
        catch (KeyNotFoundException ex) { return NotFound(APIResponse.Fail(ex.Message, 404)); }
        catch (UnauthorizedAccessException ex) { return StatusCode(403, APIResponse.Fail(ex.Message, 403)); }
    }
}
