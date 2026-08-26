using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO;
using MV.DomainLayer.DTO.ResponseModel;
using MV.PresentationLayer.Helpers;

namespace MV.PresentationLayer.Controllers;

/// <summary>
/// The signed-in user's saved-tutor list.
/// </summary>
/// <remarks>
/// The owner always comes from the token, never the route or body — a wishlist is private, and
/// there is no reason for one account to read or edit another's.
///
/// Parent and Student only: they are the ones who shop for a tutor, and they are the only roles
/// with a favorites page to read the list back (see the routes under /parent-portal and
/// /student-portal). A tutor or admin saving a row here could never see or manage it again, so
/// the write is refused rather than silently orphaned.
/// </remarks>
[ApiController]
[Route("api/favorites/tutors")]
[Authorize(Roles = UserRole.ParentOrStudent)]
public class TutorFavoriteController : ControllerBase
{
    private readonly ITutorFavoriteService _favoriteService;

    public TutorFavoriteController(ITutorFavoriteService favoriteService)
    {
        _favoriteService = favoriteService;
    }

    /// <summary>Saved tutors with enough detail to render cards, newest save first.</summary>
    [HttpGet]
    public async Task<ActionResult<APIResponse<List<TutorFavoriteResponse>>>> GetFavorites(CancellationToken ct)
    {
        var result = await _favoriteService.GetFavoritesAsync(UserHelper.GetUserId(User), ct);
        return Ok(APIResponse<List<TutorFavoriteResponse>>.Success(result, "Lấy danh sách yêu thích thành công."));
    }

    /// <summary>
    /// Just the saved ids — what the search and detail pages need to paint the heart, without
    /// pulling every tutor's card data on page load.
    /// </summary>
    [HttpGet("ids")]
    public async Task<ActionResult<APIResponse<List<string>>>> GetFavoriteIds(CancellationToken ct)
    {
        var result = await _favoriteService.GetFavoriteTutorIdsAsync(UserHelper.GetUserId(User), ct);
        return Ok(APIResponse<List<string>>.Success(result, "Lấy danh sách yêu thích thành công."));
    }

    /// <summary>
    /// Saves the tutor, or un-saves them if already saved. Returns the state after the toggle so
    /// the client can settle on the server's answer rather than assuming its own flip stuck.
    /// </summary>
    [HttpPost("{tutorId}/toggle")]
    public async Task<ActionResult<APIResponse<bool>>> ToggleFavorite(string tutorId, CancellationToken ct)
    {
        try
        {
            var saved = await _favoriteService.ToggleFavoriteAsync(UserHelper.GetUserId(User), tutorId, ct);
            return Ok(APIResponse<bool>.Success(
                saved, saved ? "Đã lưu vào danh sách yêu thích." : "Đã bỏ khỏi danh sách yêu thích."));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(APIResponse<object>.Fail(ex.Message, 400));
        }
    }
}
