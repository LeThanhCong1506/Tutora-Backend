using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.DTO;
using MV.DomainLayer.DTO.ResponseModel;
using MV.PresentationLayer.Helpers;

namespace MV.PresentationLayer.Controllers;

/// <summary>
/// Lets a user see the warnings held against their own account.
/// </summary>
/// <remarks>
/// The equivalent admin routes live under <c>api/admin/warnings</c> behind
/// <c>warning.view</c>. A user must be able to read their own record without that permission,
/// and must never be able to read anyone else's — so the id comes from the token, never the route.
/// </remarks>
[ApiController]
[Route("api/warnings")]
[Authorize]
public class MyWarningsController : ControllerBase
{
    private readonly IWarningService _warningService;

    public MyWarningsController(IWarningService warningService)
    {
        _warningService = warningService;
    }

    /// <summary>
    /// The caller's own warning history plus their current suspension, if any.
    /// </summary>
    [HttpGet("me")]
    public async Task<ActionResult<APIResponse<UserWarningSummaryResponse>>> GetMyWarnings()
    {
        var userId = UserHelper.GetUserId(User);
        var result = await _warningService.GetUserWarningsAsync(userId);
        return Ok(APIResponse<UserWarningSummaryResponse>.Success(result, "Lấy cảnh báo của bạn thành công."));
    }

    /// <summary>
    /// The caller's own suspension history, newest first — including suspensions already lifted.
    /// </summary>
    [HttpGet("me/suspensions")]
    public async Task<ActionResult<APIResponse<List<SuspensionListResponse>>>> GetMySuspensions()
    {
        var userId = UserHelper.GetUserId(User);
        var result = await _warningService.GetUserSuspensionsAsync(userId);
        return Ok(APIResponse<List<SuspensionListResponse>>.Success(result, "Lấy lịch sử đình chỉ của bạn thành công."));
    }
}
