using MV.DomainLayer.Constants;
using MV.DomainLayer.Enums;

namespace MV.DomainLayer.DTO.RequestModel
{
    /// <summary>
    /// Parameters for searching and filtering tutors
    /// Supports PostgreSQL Full-Text Search with unaccent and pg_trgm
    /// Matches Figma UI design for tutor search page
    /// </summary>
    public class TutorSearchParameters
    {
        private const int MaxPageSize = 50;
        private const int DefaultPageSize = 10;

        public int PageNumber { get; set; } = 1;

        private int _pageSize = DefaultPageSize;
        public int PageSize
        {
            get => _pageSize;
            set => _pageSize = value > MaxPageSize ? MaxPageSize : value;
        }

        // ==================== SEARCH ====================
        /// <summary>
        /// Search keyword - supports fuzzy search with Vietnamese unaccent
        /// Searches in: FullName, Headline, SubjectName, Education
        /// Example: "toán", "IELTS", "luyện thi đại học"
        /// </summary>
        public string? SearchTerm { get; set; }

        // ==================== CATEGORY TABS (Figma) ====================
        /// <summary>
        /// Filter by category tab (Maps to Figma tabs: TẤT CẢ, KHOA HỌC, NGÔN NGỮ, NGHỆ THUẬT, IT & TECH)
        /// Options: "all", "science", "language", "art", "it_tech"
        /// Default: "all" (shows all categories)
        /// </summary>
        public string? Category { get; set; } = TutorSearchCategory.AllValue;

        // ==================== FILTERS ====================

        /// <summary>
        /// Filter by subject IDs (e.g., [1, 2, 3] for Math, Physics, Chemistry)
        /// </summary>
        public List<int>? SubjectIds { get; set; }

        /// <summary>
        /// Filter by grade/education level (Maps to Figma "CẤP HỌC" dropdown)
        /// Options: "tieu_hoc", "thcs", "thpt", "dai_hoc", "sau_dai_hoc", "ielts", "toeic", "sat"
        /// OR direct values: "Lớp 6", "Lớp 10", etc.
        /// Vietnamese display: Tiểu học, THCS, THPT, Đại học, Sau đại học, IELTS, TOEIC, SAT
        /// </summary>
        public string? GradeLevel { get; set; }

        /// <summary>
        /// Filter by subscription/class type (Maps to Figma "LOẠI LỚP" badge)
        /// Options: "free", "guided", "intensive", "elite"
        /// Vietnamese display: FREE TUTOR, GUIDED TUTOR, INTENSIVE TUTOR, ELITE TUTOR
        /// </summary>
        public List<string>? SubscriptionTypes { get; set; }

        /// <summary>
        /// Filter by budget range (Maps to Figma "NGÂN SÁCH" dropdown)
        /// Predefined ranges: "all", "under_50", "50_100", "100_200", "200_500", "over_500"
        /// Unit: USD per session
        /// </summary>
        public string? BudgetRange { get; set; }

        /// <summary>
        /// Minimum hourly rate (custom range)
        /// </summary>
        public decimal? MinHourlyRate { get; set; }

        /// <summary>
        /// Maximum hourly rate (custom range)
        /// </summary>
        public decimal? MaxHourlyRate { get; set; }

        /// <summary>
        /// Minimum average rating (1-5)
        /// </summary>
        public double? MinRating { get; set; }

        /// <summary>
        /// Minimum years of experience (Thâm niên)
        /// </summary>
        public int? MinYearsExperience { get; set; }

        /// <summary>
        /// Filter by teaching area city (e.g., "Ho Chi Minh", "Ha Noi")
        /// </summary>
        public string? TeachingAreaCity { get; set; }

        /// <summary>
        /// Filter by teaching area district (e.g., "Quan 1", "Quan 7", "Quận 3")
        /// </summary>
        public string? TeachingAreaDistrict { get; set; }

        /// <summary>
        /// Filter by teaching mode (Maps to Figma "HÌNH THỨC" dropdown)
        /// Options: "online", "offline", "hybrid"
        /// </summary>
        public string? TeachingMode { get; set; }

        /// <summary>
        /// Filter by tutor gender (Male = 1, Female = 2).
        /// </summary>
        public Gender? Gender { get; set; }

        /// <summary>
        /// Lọc theo LỊCH RẢNH của gia sư (tutor_availability).
        /// </summary>
        public List<int>? AvailableDaysOfWeek { get; set; }

        /// <summary>
        /// true = gia sư phải rảnh ĐỦ TẤT CẢ ngày trong <see cref="AvailableDaysOfWeek"/>
        /// ("rảnh cả T7 và CN"). false (mặc định) = rảnh MỘT TRONG số đó ("cuối tuần").
        ///
        /// Trước đây chỉ có kiểu "một trong", nên "rảnh cả T7 và CN" trả về gia sư chỉ rảnh
        /// T7 — sai kết quả mà không có dấu hiệu nào, vì trợ lý AI lại diễn đạt theo đúng
        /// lời user ("có gia sư rảnh cả Thứ 7 và Chủ Nhật ạ").
        /// </summary>
        public bool AvailableDaysMatchAll { get; set; }

        /// <summary>
        /// Khung giờ rảnh cần khớp (vd "tối" → 18:00-21:00). Chỉ lọc khi có ĐỦ cả hai đầu.
        /// Điều kiện: khoảng rảnh của gia sư phải PHỦ khung giờ này (start &lt;= from, end &gt;= to).
        /// </summary>
        public TimeOnly? AvailableFrom { get; set; }
        public TimeOnly? AvailableTo { get; set; }

        /// <summary>
        /// Filter by verification status (e.g., "verified", "pending")
        /// </summary>
        public string? VerificationStatus { get; set; }

        // ==================== SORTING (Figma: SORT BY dropdown) ====================

        /// <summary>
        /// Sort by field (Maps to Figma "SORT BY" dropdown)
        /// Options:
        /// - "rating_desc": ĐÁNH GIÁ CAO NHẤT - Highest rating first
        /// - "rating_asc": ĐÁNH GIÁ THẤP NHẤT - Lowest rating first
        /// - "price_asc": GIÁ THẤP NHẤT - Lowest price first
        /// - "price_desc": GIÁ CAO NHẤT - Highest price first
        /// - "experience_desc": THÂM NIÊN CAO NHẤT - Most experienced first
        /// - "reviews_desc": ĐÁNH GIÁ NHIỀU NHẤT - Most reviews first
        /// - "newest": MỚI NHẤT - Newest profiles first
        /// - "popularity" (default): PHỔ BIẾN NHẤT - Most popular (by bookings)
        /// </summary>
        public string? SortBy { get; set; } = TutorSearchSortBy.Default;
    }
}
