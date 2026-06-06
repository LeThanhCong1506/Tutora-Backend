namespace MV.DomainLayer.DTO.ResponseModel
{
    /// <summary>
    /// Minimal tutor identity snapshot — 4 fields for quick embedding in search results or listings.
    /// Caller: <c>IUserService.GetTutorProfileShortAsync</c> → <c>GET /api/tutor/{id}/tutor-profile-info</c>.
    /// Has <c>TutorId</c> (unlike <see cref="TutorProfileResponse"/>).
    /// For full public view, use <see cref="TutorProfilePreviewResponse"/>.
    /// </summary>
    public class TutorProfileShortResponse
    {
        public string TutorId { get; set; } = null!;
        public string? Headline { get; set; }
        public string? Bio { get; set; }
        public decimal? HourlyRate { get; set; }
    }
}
