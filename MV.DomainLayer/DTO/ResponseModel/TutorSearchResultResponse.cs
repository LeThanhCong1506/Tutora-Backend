namespace MV.DomainLayer.DTO.ResponseModel
{
    /// <summary>
    /// Response model for tutor search results - matches Figma tutor card design
    /// Contains all fields needed for the tutor card display
    /// </summary>
    public class TutorSearchResultResponse
    {
        // === User Info ===
        public string TutorId { get; set; } = null!;
        public string? FullName { get; set; }
        public string? AvatarUrl { get; set; }

        // === Profile Info ===
        /// <summary>
        /// Short headline/tagline (displayed under name in Figma)
        /// </summary>
        public string? Headline { get; set; }

        public string? Bio { get; set; }
        public string? VideoUrl { get; set; }

        /// <summary>
        /// Education institution (e.g., "Stanford University", "ĐH Khoa học Tự nhiên")
        /// Displayed with university icon in Figma
        /// </summary>
        public string? Education { get; set; }

        /// <summary>
        /// Học vị hiển thị trên thẻ gia sư. Hồ sơ mới lấy nguyên cột <c>tutor_profiles.degree</c>
        /// ("Thạc sĩ", "Tiến sĩ"...); hồ sơ cũ chưa có cột này thì suy ra từ chuỗi education
        /// và trả về dạng viết tắt in hoa ("PHD CANDIDATE", "M.SC", "MBA").
        /// Displayed as small badge next to name in Figma
        /// </summary>
        public string? DegreeLevel { get; set; }

        // === Rating & Reviews ===
        /// <summary>
        /// Average rating (1-5 stars) - displayed with star icon in Figma
        /// </summary>
        public double? AverageRating { get; set; }
        public int? TotalReviews { get; set; }

        /// <summary>
        /// Total number of classSessions (buổi học) this tutor has. Shown next to the rating on the card.
        /// </summary>
        public int TotalClassSessions { get; set; }

        /// <summary>
        /// Total number of distinct students this tutor has taught or is teaching.
        /// </summary>
        public int TotalStudents { get; set; }

        // === Pricing ===
        /// <summary>
        /// Lowest active price-per-hour across all of the tutor's subjects/grades.
        /// Displayed on the card as "Từ {MinPricePerHour}đ/giờ".
        /// </summary>
        public decimal? MinPricePerHour { get; set; }

        // === Experience (Thâm niên) ===
        /// <summary>
        /// Years of teaching experience (Thâm niên: X Năm)
        /// Displayed in Figma card as "THÂM NIÊN: 5 Năm"
        /// </summary>
        public int? YearsOfExperience { get; set; }

        /// <summary>
        /// Total completed teaching hours
        /// </summary>
        public int? CompletedHours { get; set; }

        // === Subjects & Tags ===
        /// <summary>
        /// List of subjects with their tags
        /// Displayed as pills/chips in Figma (e.g., "Toán SAT 11", "AP Calculus", "ACT Math")
        /// </summary>
        public List<TutorSubjectInfo>? Subjects { get; set; }

        // === Location & Mode ===
        public string? TeachingAreaCity { get; set; }
        public string? TeachingAreaDistrict { get; set; }
        public string? TeachingMode { get; set; }

        // === Subscription/Class Type (LOẠI LỚP badge in Figma) ===
        /// <summary>
        /// Class type badge: "free", "guided", "intensive", "elite"
        /// Displayed as colored badge: INTENSIVE TUTOR (gold), GUIDED TUTOR (green), etc.
        /// </summary>
        public string? SubscriptionType { get; set; }

        /// <summary>
        /// Display label for subscription type (e.g., "INTENSIVE TUTOR", "GUIDED TUTOR")
        /// </summary>
        public string? SubscriptionTypeLabel { get; set; }

        // === Verification ===
        public string? VerificationStatus { get; set; }

        // === Certifications (e.g., "Cambridge CELTA") ===
        /// <summary>
        /// List of certifications to display
        /// </summary>
        public List<TutorCertificateInfo>? Certifications { get; set; }

        // === Results/Success Rate ===
        /// <summary>
        /// Success rate or satisfaction rate (KẾT QUẢ/HÀI LÒNG in Figma)
        /// Displayed as "98% A/B" or "100%"
        /// </summary>
        public string? SuccessRate { get; set; }

        // === Highlights/Commitments ===
        /// <summary>
        /// List of highlights shown with checkmarks in Figma
        /// e.g., "Cam kết tiến độ qua LMS Report", "Chuyên luyện thi chứng chỉ SAT/ACT"
        /// </summary>
        public List<string>? Highlights { get; set; }

        // === Specialty (Chuyên môn đặc biệt) ===
        /// <summary>
        /// Special expertise area (CHUYÊN MÔN in Figma)
        /// e.g., "Viết luận"
        /// </summary>
        public string? Specialty { get; set; }
    }

    /// <summary>
    /// Subject information for search results
    /// </summary>
    public class TutorSubjectInfo
    {
        public int SubjectId { get; set; }
        public string? SubjectName { get; set; }

        /// <summary>
        /// Price per hour for this subject/grade offering (VND).
        /// </summary>
        public decimal? PricePerHour { get; set; }

        /// <summary>
        /// Grade levels this subject covers
        /// </summary>
        public List<string>? GradeLevels { get; set; }

        /// <summary>
        /// Tags/specific topics (e.g., "SAT 11", "AP Calculus", "ACT Math")
        /// Displayed as pills in Figma
        /// </summary>
        public List<string>? Tags { get; set; }
    }

    /// <summary>
    /// Certificate/qualification info for display
    /// </summary>
    public class TutorCertificateInfo
    {
        /// <summary>
        /// Certificate name (e.g., "Cambridge CELTA", "IELTS 8.5")
        /// </summary>
        public string? CertificateName { get; set; }

        /// <summary>
        /// Issuing organization
        /// </summary>
        public string? IssuingOrganization { get; set; }

        /// <summary>
        /// Year issued
        /// </summary>
        public int? YearIssued { get; set; }
    }

    /// <summary>
    /// Paged response wrapper for tutor search results
    /// </summary>
    public class TutorSearchPagedResponse
    {
        public List<TutorSearchResultResponse> Items { get; set; } = new();
        public int CurrentPage { get; set; }
        public int TotalPages { get; set; }
        public int PageSize { get; set; }
        public int TotalCount { get; set; }
        public bool HasPrevious => CurrentPage > 1;
        public bool HasNext => CurrentPage < TotalPages;

        /// <summary>
        /// Display text for total results (e.g., "8 KẾT QUẢ TÌM THẤY")
        /// </summary>
        public string? ResultsText { get; set; }

        // === Filter Metadata (for UI to display active filters) ===
        public TutorSearchFilterMetadata? FilterMetadata { get; set; }
    }

    /// <summary>
    /// Metadata about available filter options - matches Figma filter UI
    /// Helps UI to populate filter dropdowns dynamically
    /// </summary>
    public class TutorSearchFilterMetadata
    {
        // === Category Tabs (Figma: TẤT CẢ, KHOA HỌC, NGÔN NGỮ, NGHỆ THUẬT, IT & TECH) ===
        /// <summary>
        /// Available categories for tabs
        /// </summary>
        public List<FilterOption>? AvailableCategories { get; set; }

        // === Grade Level Dropdown (Figma: CẤP HỌC) ===
        /// <summary>
        /// Available grade levels (Tiểu học, THCS, THPT, Đại học, etc.)
        /// </summary>
        public List<FilterOption>? AvailableGradeLevels { get; set; }

        // === Budget Dropdown (Figma: NGÂN SÁCH) ===
        /// <summary>
        /// Available budget ranges
        /// </summary>
        public List<FilterOption>? AvailableBudgetRanges { get; set; }

        // === Teaching Mode Dropdown (Figma: HÌNH THỨC) ===
        /// <summary>
        /// Available teaching modes (Online, Offline, Hybrid)
        /// </summary>
        public List<FilterOption>? AvailableTeachingModes { get; set; }

        // === Sort Options (Figma: SORT BY) ===
        /// <summary>
        /// Available sort options
        /// </summary>
        public List<FilterOption>? AvailableSortOptions { get; set; }

        /// <summary>
        /// Available subjects with count
        /// </summary>
        public List<FilterOption>? AvailableSubjects { get; set; }

        /// <summary>
        /// Available cities with count
        /// </summary>
        public List<FilterOption>? AvailableCities { get; set; }

        /// <summary>
        /// Price range in the result set
        /// </summary>
        public decimal? MinPriceInResults { get; set; }
        public decimal? MaxPriceInResults { get; set; }

        /// <summary>
        /// Rating range in the result set
        /// </summary>
        public double? MinRatingInResults { get; set; }
        public double? MaxRatingInResults { get; set; }
    }

    /// <summary>
    /// Generic filter option with count
    /// </summary>
    public class FilterOption
    {
        public string Value { get; set; } = null!;
        public string Label { get; set; } = null!;
        public int Count { get; set; }
    }

    /// <summary>
    /// Trending search terms for the search bar
    /// </summary>
    public class TrendingSearchResponse
    {
        /// <summary>
        /// List of trending search terms (e.g., "SAT PREP", "CALCULUS III", "UX DESIGN")
        /// </summary>
        public List<string> TrendingTerms { get; set; } = new();

        /// <summary>
        /// Placeholder text for search input
        /// </summary>
        public string? SearchPlaceholder { get; set; }
    }
}
