using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.PresentationLayer.Authorization;

namespace MV.PresentationLayer.Controllers;

/// <summary>
/// Admin chỉnh ngưỡng gợi ý gia sư trong app giải bài tập.
/// </summary>
[ApiController]
[Route("api/admin/tutor-suggestion")]
[Authorize]
public class AdminTutorSuggestionController(
    ITutorSuggestionService tutorSuggestionService) : ControllerBase
{
    private string? UserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    [HttpGet("settings")]
    [RequirePermission(Permissions.LookupView)]
    public async Task<IActionResult> GetSettings(CancellationToken ct)
    {
        var result = await tutorSuggestionService.GetSettingsAsync(ct);
        return Ok(APIResponse<TutorSuggestionSettingsResponse>.Success(result, "Lấy cấu hình thành công."));
    }

    [HttpPut("settings")]
    [RequirePermission(Permissions.LookupUpdate)]
    public async Task<IActionResult> UpdateSettings(
        [FromBody] TutorSuggestionSettingsRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return BadRequest(APIResponse<object>.Fail(ApiMessages.InvalidInputData, 400));

        var result = await tutorSuggestionService.UpdateSettingsAsync(request, UserId, ct);
        return Ok(APIResponse<TutorSuggestionSettingsResponse>.Success(result, "Cập nhật cấu hình thành công."));
    }
}
