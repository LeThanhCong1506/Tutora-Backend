namespace MV.DomainLayer.DTO.ResponseModel
{
    /// <summary>
    /// Public tutor profile card — returned for search/listing pages, cached 15 minutes.
    /// Caller: <c>ITutorVerificationService.GetTutorProfilePreviewAsync</c>.
    /// Requires tutor status = Active. Includes subjects and certificates but NOT
    /// availability schedule, feedbacks, or active classes.
    /// For the full landing page, use <see cref="TutorFullProfileResponse"/>.
    /// </summary>
    public class TutorProfilePreviewResponse
    {
        // Video
        public string? VideoIntroUrl { get; set; }

        // Basic Info
        public string? AvatarUrl { get; set; }
        public string? FullName { get; set; }
        public string? Headline { get; set; }
        public double? AverageRating { get; set; }
        public int? TotalReviews { get; set; }
        public string? TeachingAreaCity { get; set; }
        public string? TeachingAreaDistrict { get; set; }
        public string? TeachingMode { get; set; }
        public List<SubjectInfo>? Subjects { get; set; }

        // Introduction
        public string? Bio { get; set; }
        public string? Degree { get; set; }
        public string? Education { get; set; }
        public double? Gpa { get; set; }
        public double? GpaScale { get; set; }
        public string? Experience { get; set; }

        // Certificates
        public List<CertificateResponse>? Certificates { get; set; }

    }
}
