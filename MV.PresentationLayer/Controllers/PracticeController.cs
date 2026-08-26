using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.DTO;
using MV.DomainLayer.DTO.RequestModel.Practice;
using MV.DomainLayer.DTO.ResponseModel.Practice;
using MV.PresentationLayer.Helpers;

namespace MV.PresentationLayer.Controllers;

/// <summary>
/// Vòng luyện tập: giải xong 1 bài -> làm thử 1 câu tương tự từ question bank.
/// KHÔNG trừ credit — luyện tập phải miễn phí thì học sinh mới chịu làm.
/// </summary>
[ApiController]
[Route("api/practice")]
[Authorize]
public class PracticeController(IPracticeService service) : ControllerBase
{
    /// <summary>GET /api/practice/next?chapter=can_bac_hai</summary>
    [HttpGet("next")]
    public async Task<IActionResult> GetNext(
        [FromQuery] string? chapter,
        [FromQuery] string? questionText,
        [FromQuery] string? difficulty,
        CancellationToken ct)
    {
        var userId = UserHelper.GetUserId(User);
        var result = await service.GetNextAsync(userId, chapter, questionText, difficulty, ct);

        // Không có câu nào KHÔNG phải lỗi — chương chưa có câu chấm được, hoặc đã làm hết.
        return result == null
            ? Ok(APIResponse<PracticeQuestionResponse?>.Success(null, "Chưa có câu luyện phù hợp."))
            : Ok(APIResponse<PracticeQuestionResponse>.Success(result, "Lấy câu luyện thành công."));
    }

    /// <summary>POST /api/practice/submit</summary>
    [HttpPost("submit")]
    public async Task<IActionResult> Submit([FromBody] SubmitPracticeRequest request, CancellationToken ct)
    {
        var userId = UserHelper.GetUserId(User);
        var result = await service.SubmitAsync(userId, request, ct);

        return result == null
            ? NotFound(APIResponse.Fail("Không tìm thấy câu luyện này.", 404))
            : Ok(APIResponse<PracticeResultResponse>.Success(result, "Chấm bài thành công."));
    }
}
