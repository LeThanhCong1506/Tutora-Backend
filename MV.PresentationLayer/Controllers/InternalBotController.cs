using Microsoft.AspNetCore.Mvc;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.DTO.RequestModel;

namespace MV.PresentationLayer.Controllers;

/// <summary>
/// Internal API for Tutora Zalo bot — protected by X-Internal-Key header.
/// NOT exposed to end users; used only by the bot service.
/// </summary>
[ApiController]
[Route("internal")]
public class InternalBotController : ControllerBase
{
    private readonly ITutorSearchService _tutorSearch;
    private readonly string? _internalKey;

    private static readonly string[] Tiers = ["free", "guided", "intensive", "elite"];

    public InternalBotController(ITutorSearchService tutorSearch, IConfiguration config)
    {
        _tutorSearch = tutorSearch;
        _internalKey = config["InternalBot:Key"];
    }

    // ── GET /internal/tutors/match ────────────────────────────────────────────
    // Returns up to 3 tutors — one per tier (standard → pro → premium).
    // The bot shows tier-based results without asking budget upfront.
    [HttpGet("tutors/match")]
    public async Task<IActionResult> MatchTutors(
        [FromQuery] int? subjectId,
        [FromQuery] string? gradeLevel,
        [FromQuery] string? teachingMode,
        [FromQuery] string? district,
        [FromQuery] string? city,
        [FromQuery] decimal? minRate,
        [FromQuery] decimal? maxRate)
    {
        if (!IsAuthorized()) return Unauthorized();

        var results = new List<object>();

        foreach (var tier in Tiers)
        {
            var parameters = new TutorSearchParameters
            {
                PageNumber = 1,
                PageSize = 1,
                SubjectIds = subjectId.HasValue ? [subjectId.Value] : null,
                GradeLevel = gradeLevel,
                TeachingMode = teachingMode,
                TeachingAreaDistrict = district,
                TeachingAreaCity = city,
                MinHourlyRate = minRate,
                MaxHourlyRate = maxRate,
                SubscriptionTypes = [tier],
                SortBy = "rating_desc",
            };

            var paged = await _tutorSearch.SearchTutorsAsync(parameters);
            if (paged.Items.Count > 0)
                results.Add(paged.Items[0]);
        }

        return Ok(new { tutors = results });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private bool IsAuthorized()
    {
        if (string.IsNullOrEmpty(_internalKey)) return true; // dev: no key configured
        Request.Headers.TryGetValue("X-Internal-Key", out var provided);
        return provided == _internalKey;
    }
}
