using MV.DomainLayer.DTO.ResponseModel;

namespace MV.ApplicationLayer.ServiceInterfaces;

/// <summary>
/// A user's saved-tutor list. Stored per account rather than per device, so it follows the
/// person between web and the Zalo Mini App and survives clearing the browser.
/// </summary>
public interface ITutorFavoriteService
{
    /// <summary>The caller's saved tutors, newest save first.</summary>
    Task<List<TutorFavoriteResponse>> GetFavoritesAsync(string userId, CancellationToken ct = default);

    /// <summary>
    /// Just the ids, for painting the heart on search/detail cards without loading the
    /// whole list.
    /// </summary>
    Task<List<string>> GetFavoriteTutorIdsAsync(string userId, CancellationToken ct = default);

    /// <summary>
    /// Saves the tutor, or removes them if already saved. Returns the state after the toggle:
    /// <c>true</c> = now saved.
    /// </summary>
    Task<bool> ToggleFavoriteAsync(string userId, string tutorId, CancellationToken ct = default);
}
