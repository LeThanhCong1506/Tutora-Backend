using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using System.Security.Claims;

namespace MV.PresentationLayer.Controllers;

/// <summary>
/// Note do học sinh tạo từ Interactive Solution Canvas (question_notes).
/// Tách hoàn toàn với /api/ai-chat (lịch sử hội thoại).
/// </summary>
[ApiController]
[Route("api/question-notes")]
[Authorize]
public class QuestionNoteController : ControllerBase
{
    private readonly IQuestionNoteService _service;

    public QuestionNoteController(IQuestionNoteService service)
    {
        _service = service;
    }

    private string? UserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    [HttpGet]
    public async Task<IActionResult> GetMyNotes([FromQuery] string? subject = null, [FromQuery] int? gradeLevel = null)
    {
        if (string.IsNullOrEmpty(UserId))
            return Unauthorized(APIResponse.Fail(ApiMessages.Unauthorized, 401));

        var result = await _service.GetMyNotesAsync(UserId, subject, gradeLevel);
        return Ok(APIResponse<List<QuestionNoteResponse>>.Success(result, "Lấy danh sách note thành công."));
    }

    [HttpGet("{noteId}")]
    public async Task<IActionResult> GetNote(Guid noteId)
    {
        if (string.IsNullOrEmpty(UserId))
            return Unauthorized(APIResponse.Fail(ApiMessages.Unauthorized, 401));

        var result = await _service.GetNoteAsync(UserId, noteId);
        return Ok(APIResponse<QuestionNoteResponse>.Success(result, "Lấy note thành công."));
    }

    [HttpPost]
    public async Task<IActionResult> CreateNote([FromBody] QuestionNoteCreateRequest dto)
    {
        if (string.IsNullOrEmpty(UserId))
            return Unauthorized(APIResponse.Fail(ApiMessages.Unauthorized, 401));

        if (!ModelState.IsValid)
            return BadRequest(APIResponse.Fail("Dữ liệu đầu vào không hợp lệ.", 400));

        var result = await _service.CreateNoteAsync(UserId, dto);
        return Ok(APIResponse<QuestionNoteResponse>.Success(result, "Đã lưu note."));
    }

    [HttpPut("{noteId}")]
    public async Task<IActionResult> UpdateNote(Guid noteId, [FromBody] QuestionNoteUpdateRequest dto)
    {
        if (string.IsNullOrEmpty(UserId))
            return Unauthorized(APIResponse.Fail(ApiMessages.Unauthorized, 401));

        var result = await _service.UpdateNoteAsync(UserId, noteId, dto);
        return Ok(APIResponse<QuestionNoteResponse>.Success(result, "Đã cập nhật note."));
    }

    [HttpDelete("{noteId}")]
    public async Task<IActionResult> DeleteNote(Guid noteId)
    {
        if (string.IsNullOrEmpty(UserId))
            return Unauthorized(APIResponse.Fail(ApiMessages.Unauthorized, 401));

        await _service.DeleteNoteAsync(UserId, noteId);
        return Ok(APIResponse.Success("Đã xoá note."));
    }
}
