namespace MV.DomainLayer.DTO.ResponseModel
{
    /// <summary>
    /// Tutor's editable profile — returned when a tutor reads their own profile for editing.
    /// Caller: <c>ITutorService.GetTutorProfileAsync</c> → <c>GET /api/tutor?tutorId=...</c>.
    /// Contains only write-able profile fields; no identity (TutorId) or public info (Subjects, Feedbacks).
    /// For public display, use <see cref="TutorProfilePreviewResponse"/> or <see cref="TutorFullProfileResponse"/>.
    /// </summary>
    public class TutorProfileResponse
    {
        public string? Headline { get; set; }
        public string? Bio { get; set; }
        public string? Education { get; set; }
        public string? Experience { get; set; }
        public double? Gpa { get; set; }
        public double? GpaScale { get; set; }

        public string? VideoIntroUrl { get; set; }

        public string? TeachingMode { get; set; }
        public string? TeachingAreaCity { get; set; }
        public string? TeachingAreaDistrict { get; set; }
    }
}
