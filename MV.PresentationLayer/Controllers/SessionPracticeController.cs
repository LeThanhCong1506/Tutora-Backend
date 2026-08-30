using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.DTO;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Exceptions;
using MV.PresentationLayer.Helpers;

namespace MV.PresentationLayer.Controllers;

/// <summary>
/// Bài tập nhanh gia sư tạo trong buổi học từ tài liệu của khoá.
/// Gia sư: tạo (AI sinh nháp) -> sửa/xoá câu -> gửi. Học sinh: xem bộ đã gửi và trả lời.
/// </summary>
[ApiController]
[Route("api")]
[Authorize]
public class SessionPracticeController(ISessionPracticeService practiceService) : ControllerBase
{
    /// <summary>
    /// Danh sách bộ bài tập của booking. Gia sư thấy cả nháp; học sinh chỉ thấy bộ
    /// đã gửi (kèm bài làm của chính em).
    /// </summary>
    [HttpGet("bookings/{bookingId:int}/practice-sets")]
    public async Task<IActionResult> GetSets(int bookingId)
    {
        try
        {
            var actorId = UserHelper.GetUserId(User);
            var result = await practiceService.GetSetsAsync(bookingId, actorId);
            return Ok(APIResponse<List<SessionPracticeSetResponse>>.Success(result, "Lấy danh sách bài tập thành công."));
        }
        catch (NotFoundException ex)
        {
            return NotFound(APIResponse.Fail(ex.Message, 404));
        }
        catch (BadRequestException ex)
        {
            return BadRequest(APIResponse.Fail(ex.Message));
        }
    }

    /// <summary>Gia sư bấm "Tạo câu hỏi" — AI đọc tài liệu đã chọn và sinh bộ NHÁP.</summary>
    [HttpPost("bookings/{bookingId:int}/practice-sets")]
    public async Task<IActionResult> Generate(int bookingId, [FromBody] GenerateSessionPracticeRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(APIResponse.Fail("Dữ liệu không hợp lệ."));

        try
        {
            var tutorId = UserHelper.GetUserId(User);
            var result = await practiceService.GenerateAsync(bookingId, tutorId, request);
            return Ok(APIResponse<SessionPracticeSetResponse>.Success(result, "Tạo câu hỏi thành công."));
        }
        catch (NotFoundException ex)
        {
            return NotFound(APIResponse.Fail(ex.Message, 404));
        }
        catch (BadRequestException ex)
        {
            return BadRequest(APIResponse.Fail(ex.Message));
        }
    }

    /// <summary>Gia sư sửa 1 câu — chỉ khi bộ còn nháp.</summary>
    [HttpPut("practice-questions/{questionId:guid}")]
    public async Task<IActionResult> UpdateQuestion(Guid questionId, [FromBody] UpdateSessionPracticeQuestionRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(APIResponse.Fail("Dữ liệu không hợp lệ."));

        try
        {
            var tutorId = UserHelper.GetUserId(User);
            var result = await practiceService.UpdateQuestionAsync(questionId, tutorId, request);
            return Ok(APIResponse<SessionPracticeQuestionResponse>.Success(result, "Cập nhật câu hỏi thành công."));
        }
        catch (NotFoundException ex)
        {
            return NotFound(APIResponse.Fail(ex.Message, 404));
        }
        catch (BadRequestException ex)
        {
            return BadRequest(APIResponse.Fail(ex.Message));
        }
    }

    /// <summary>Gia sư xoá 1 câu — chỉ khi bộ còn nháp.</summary>
    [HttpDelete("practice-questions/{questionId:guid}")]
    public async Task<IActionResult> DeleteQuestion(Guid questionId)
    {
        try
        {
            var tutorId = UserHelper.GetUserId(User);
            await practiceService.DeleteQuestionAsync(questionId, tutorId);
            return Ok(APIResponse.Success("Xoá câu hỏi thành công."));
        }
        catch (NotFoundException ex)
        {
            return NotFound(APIResponse.Fail(ex.Message, 404));
        }
        catch (BadRequestException ex)
        {
            return BadRequest(APIResponse.Fail(ex.Message));
        }
    }

    /// <summary>Gia sư gửi RIÊNG 1 câu — câu còn lại vẫn ở trạng thái nháp.</summary>
    [HttpPost("practice-questions/{questionId:guid}/send")]
    public async Task<IActionResult> SendQuestion(Guid questionId)
    {
        try
        {
            var tutorId = UserHelper.GetUserId(User);
            var result = await practiceService.SendQuestionAsync(questionId, tutorId);
            return Ok(APIResponse<SessionPracticeQuestionResponse>.Success(result, "Đã gửi câu hỏi cho học sinh."));
        }
        catch (NotFoundException ex)
        {
            return NotFound(APIResponse.Fail(ex.Message, 404));
        }
        catch (BadRequestException ex)
        {
            return BadRequest(APIResponse.Fail(ex.Message));
        }
    }

    /// <summary>Gửi mọi câu CHƯA gửi trong bộ.</summary>
    [HttpPost("practice-sets/{setId:guid}/send")]
    public async Task<IActionResult> Send(Guid setId)
    {
        try
        {
            var tutorId = UserHelper.GetUserId(User);
            var result = await practiceService.SendAsync(setId, tutorId);
            return Ok(APIResponse<SessionPracticeSetResponse>.Success(result, "Đã gửi bài tập cho học sinh."));
        }
        catch (NotFoundException ex)
        {
            return NotFound(APIResponse.Fail(ex.Message, 404));
        }
        catch (BadRequestException ex)
        {
            return BadRequest(APIResponse.Fail(ex.Message));
        }
    }

    /// <summary>Học sinh trả lời 1 câu. Trắc nghiệm chấm ngay; làm lại thì ghi đè.</summary>
    [HttpPost("practice-questions/{questionId:guid}/answer")]
    public async Task<IActionResult> SubmitAnswer(Guid questionId, [FromBody] SubmitSessionPracticeAnswerRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(APIResponse.Fail("Dữ liệu không hợp lệ."));

        try
        {
            var studentId = UserHelper.GetUserId(User);
            var result = await practiceService.SubmitAnswerAsync(questionId, studentId, request);
            return Ok(APIResponse<SessionPracticeAnswerResponse>.Success(result, "Đã lưu bài làm."));
        }
        catch (NotFoundException ex)
        {
            return NotFound(APIResponse.Fail(ex.Message, 404));
        }
        catch (BadRequestException ex)
        {
            return BadRequest(APIResponse.Fail(ex.Message));
        }
    }
}
