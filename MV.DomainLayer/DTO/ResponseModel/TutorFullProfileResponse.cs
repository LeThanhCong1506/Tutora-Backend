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
        public List<TutorSubjectGradePriceResponse>? SubjectGradePrices { get; set; }

        // --- Introduction ---
        public string? Bio { get; set; }
        public string? Education { get; set; }
        public double? Gpa { get; set; }
        public double? GpaScale { get; set; }
        public string? Experience { get; set; }

        // --- Certificates (chỉ trả về verified) ---
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

        // --- Teaching Stats ---
        /// <summary>Tổng số buổi gia sư đã và đang dạy (không tính buổi bị hủy).</summary>
        public int TotalClassSessions { get; set; }

        /// <summary>Tổng số học sinh gia sư đã và đang dạy (distinct, không tính booking bị hủy).</summary>
        public int TotalStudents { get; set; }
    }
}
