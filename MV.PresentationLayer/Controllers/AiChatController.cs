using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using System.Security.Claims;

namespace MV.PresentationLayer.Controllers;

/// <summary>
/// Lịch sử hội thoại người dùng ↔ AI (chat giải toán + chat tư vấn matching).
/// Tách hoàn toàn với /api/chat (chat người-người).
/// </summary>
[ApiController]
[Route("api/ai-chat")]
[Authorize]
public class AiChatController : ControllerBase
{
    private readonly IAiChatService _aiChatService;

    public AiChatController(IAiChatService aiChatService)
    {
        _aiChatService = aiChatService;
    }

    private string? UserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    [HttpGet("sessions")]
    public async Task<IActionResult> GetMySessions([FromQuery] string? chatType = null)
    {
        if (string.IsNullOrEmpty(UserId))
            return Unauthorized(APIResponse.Fail(ApiMessages.Unauthorized, 401));

        var result = await _aiChatService.GetMySessionsAsync(UserId, chatType);
        return Ok(APIResponse<List<AiChatSessionResponse>>.Success(result, "Lấy danh sách phiên chat AI thành công."));
    }

    [HttpPost("sessions")]
    public async Task<IActionResult> CreateSession([FromBody] AiChatSessionCreateRequest dto)
    {
        if (string.IsNullOrEmpty(UserId))
            return Unauthorized(APIResponse.Fail(ApiMessages.Unauthorized, 401));

        if (!ModelState.IsValid)
            return BadRequest(APIResponse.Fail("Dữ liệu đầu vào không hợp lệ.", 400));

        var result = await _aiChatService.CreateSessionAsync(UserId, dto);
        return Ok(APIResponse<AiChatSessionResponse>.Success(result, "Tạo phiên chat AI thành công."));
    }

    [HttpGet("sessions/{sessionId}/messages")]
    public async Task<IActionResult> GetMessages(Guid sessionId, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        if (string.IsNullOrEmpty(UserId))
            return Unauthorized(APIResponse.Fail(ApiMessages.Unauthorized, 401));

        var result = await _aiChatService.GetMessagesAsync(UserId, sessionId, page, pageSize);
        return Ok(APIResponse<PagedList<AiChatMessageResponse>>.Success(result, "Lấy tin nhắn thành công."));
    }

    [HttpPost("sessions/{sessionId}/messages")]
    public async Task<IActionResult> AddMessage(Guid sessionId, [FromBody] AiChatMessageCreateRequest dto)
    {
        if (string.IsNullOrEmpty(UserId))
            return Unauthorized(APIResponse.Fail(ApiMessages.Unauthorized, 401));

        if (!ModelState.IsValid)
            return BadRequest(APIResponse.Fail("Dữ liệu đầu vào không hợp lệ.", 400));

        var result = await _aiChatService.AddMessageAsync(UserId, sessionId, dto);
        return Ok(APIResponse<AiChatMessageResponse>.Success(result, "Lưu tin nhắn thành công."));
    }

    [HttpDelete("sessions/{sessionId}")]
    public async Task<IActionResult> DeleteSession(Guid sessionId)
    {
        if (string.IsNullOrEmpty(UserId))
            return Unauthorized(APIResponse.Fail(ApiMessages.Unauthorized, 401));

        await _aiChatService.DeleteSessionAsync(UserId, sessionId);
        return Ok(APIResponse.Success("Đã xoá phiên chat AI."));
    }

    [HttpDelete("sessions")]
    public async Task<IActionResult> DeleteAllSessions([FromQuery] string? chatType = null)
    {
        if (string.IsNullOrEmpty(UserId))
            return Unauthorized(APIResponse.Fail(ApiMessages.Unauthorized, 401));

        var count = await _aiChatService.DeleteAllSessionsAsync(UserId, chatType);
        return Ok(APIResponse.Success($"Đã xoá {count} phiên chat AI."));
    }

    /// <summary>
    /// POST /api/ai-chat/sessions/{id}/solve — hỏi AI giải toán (SSE streaming).
    /// .NET lưu user message, proxy stream từ tutora-ai về FE, rồi tự lưu assistant message.
    /// FE chỉ cần gọi 1 lần và đọc stream như SSE.
    /// </summary>
    [HttpPost("sessions/{sessionId}/solve")]
    public async Task Solve(Guid sessionId, [FromBody] AiSolveRequest dto, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(UserId))
        {
            Response.StatusCode = 401;
            return;
        }

        Response.ContentType = "text/event-stream";
        Response.Headers["Cache-Control"] = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";

        await foreach (var line in _aiChatService.SolveStreamAsync(UserId, sessionId, dto, ct))
        {
            // Mỗi "data: {...}" là một SSE event hoàn chỉnh → kết thúc bằng "\n\n"
            await Response.WriteAsync(line + "\n\n", ct);
            await Response.Body.FlushAsync(ct);
        }
    }
}
