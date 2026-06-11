namespace MV.DomainLayer.DTO.ResponseModel
{
    /// <summary>
    /// Complete tutor landing page payload — the heaviest public read model, cached 20 minutes.
    /// Caller: <c>ITutorVerificationService.GetTutorFullProfileAsync</c>
    ///   → <c>GET /api/tutor/{id}/full-profile-landing-page</c>.
    /// Extends <see cref="TutorProfilePreviewResponse"/> concept with weekly availability schedule,
    /// feedback list + statistics, and active booking summaries.
    /// </summary>
    public class TutorFullProfileResponse
    {
        // --- Video ---
        public string? VideoIntroUrl { get; set; }

        // --- Basic Info ---
        public string? AvatarUrl { get; set; }
        public string? FullName { get; set; }
        public string? Headline { get; set; }
        public string? TeachingAreaCity { get; set; }
        public string? TeachingAreaDistrict { get; set; }
        public string? TeachingMode { get; set; }
        public List<SubjectInfo>? Subjects { get; set; }
        public List<TutorSubjectGradePriceResponse>? SubjectGradePrices { get; set; }

        // --- Introduction ---
        public string? Bio { get; set; }
        public string? Education { get; set; }
        public double? Gpa { get; set; }
        public double? GpaScale { get; set; }
        public string? Experience { get; set; }

        // --- Certificates ---
        public List<CertificateResponse>? Certificates { get; set; }

        // --- Schedule (Tutor Availability) ---
        public List<TutorAvailabilityResponse>? Availabilities { get; set; }

        // --- Packages ---
        public List<TutorPackageResponse>? Packages { get; set; }

        // --- Feedback Statistics ---
        public int TotalFeedbacks { get; set; }
        public double AverageRating { get; set; }

        // --- Feedback List ---
        public List<FeedbackItemResponse>? Feedbacks { get; set; }

        // --- Active Classes ---
        public int TotalActiveClasses { get; set; }
        public List<ActiveClassSummary>? ActiveClasses { get; set; }
    }

    /// <summary>
    /// Summary of an active class (booking) for public tutor profile display
    /// </summary>
    public class ActiveClassSummary
    {
        public int BookingId { get; set; }
        public string? SubjectName { get; set; }
        public string? StudentName { get; set; }
        public int TotalLessons { get; set; }
        public int CompletedLessons { get; set; }
        public string? Status { get; set; }
        public DateTime? StartDate { get; set; }
    }
}
