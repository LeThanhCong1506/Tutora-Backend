using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.DTO;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;

namespace MV.PresentationLayer.Controllers
{
    /// <summary>
    /// Controller for tutor search operations
    /// Provides a single, powerful search API with all filter capabilities
    /// Supports fuzzy search with Vietnamese language (unaccent)
    /// Matches Figma UI design for tutor search page
    /// </summary>
    [Route("api/tutors/search")]
    [ApiController]
    public class TutorSearchController : ControllerBase
    {
        private readonly ITutorSearchService _tutorSearchService;

        public TutorSearchController(ITutorSearchService tutorSearchService)
        {
            _tutorSearchService = tutorSearchService;
        }

        /// <summary>
        /// Search and filter tutors with pagination
        /// </summary>
        /// <remarks>
        /// **Đây là API duy nhất để tìm kiếm và lọc Tutor.**
        /// 
        /// Hỗ trợ tất cả các bộ lọc theo Figma UI:
        ///
        /// **Search Bar:**
        /// - SearchTerm: Tìm kiếm mờ theo tên, headline, giáo dục, môn học (hỗ trợ tiếng Việt không dấu)
        ///
        /// **Category Tabs (TẤT CẢ, KHOA HỌC, NGÔN NGỮ, NGHỆ THUẬT, IT &amp; TECH):**
        /// - Category: "all", "science", "language", "art", "it_tech"
        ///
        /// **Filter Dropdowns:**
        /// - GradeLevel: "tieu_hoc", "thcs", "thpt", "dai_hoc", "sau_dai_hoc", "ielts", "toeic", "sat" hoặc trực tiếp "Lớp 6", "Lớp 10"...
        /// - BudgetRange: "all", "under_50", "50_100", "100_200", "200_500", "over_500"
        /// - TeachingMode: "online", "offline", "hybrid"
        /// - SortBy: "rating_desc", "price_asc", "price_desc", "experience_desc", "reviews_desc", "newest", "popularity"
        ///
        /// **Location Filters:**
        /// - TeachingAreaCity: Tên thành phố (ví dụ: "Hà Nội", "Hồ Chí Minh")
        /// - TeachingAreaDistrict: Tên quận/huyện (ví dụ: "Quận 1", "Quận 3")
        ///
        /// **Advanced Filters:**
        /// - SubjectIds: Lọc theo ID môn học (comma-separated, ví dụ: 1,2,3)
        /// - SubscriptionTypes: Lọc theo loại tutor (free, guided, intensive, elite)
        /// - MinHourlyRate/MaxHourlyRate: Khoảng giá tùy chỉnh
        /// - MinRating: Đánh giá tối thiểu (1-5)
        /// - MinYearsExperience: Kinh nghiệm tối thiểu (năm)
        ///
        /// **Ví dụ sử dụng:**
        /// - Tìm theo quận: /api/tutors/search?TeachingAreaDistrict=Quận 3
        /// - Tìm theo cấp học: /api/tutors/search?GradeLevel=thcs
        /// - Tìm theo môn học: /api/tutors/search?SubjectIds=1,2,3
        /// - Tìm theo ngân sách: /api/tutors/search?BudgetRange=50_100
        /// - Tìm theo hình thức: /api/tutors/search?TeachingMode=online
        /// - Tìm theo loại tutor: /api/tutors/search?SubscriptionTypes=elite
        /// - Kết hợp nhiều filter: /api/tutors/search?GradeLevel=thpt&amp;TeachingMode=online&amp;BudgetRange=100_200
        ///
        /// **Response bao gồm:**
        /// - Items: Danh sách tutor
        /// - FilterMetadata: Các options cho dropdown (chỉ trả về ở trang 1)
        /// - Pagination info: TotalCount, TotalPages, CurrentPage, PageSize
        /// </remarks>
        /// <param name="parameters">Các tham số tìm kiếm và lọc</param>
        /// <returns>Danh sách tutor với phân trang và filter metadata</returns>
        [AllowAnonymous]
        [HttpGet]
        [ProducesResponseType(typeof(APIResponse<TutorSearchPagedResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(APIResponse<object>), StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> SearchTutors([FromQuery] TutorSearchParameters parameters)
        {
            try
            {
                var result = await _tutorSearchService.SearchTutorsAsync(parameters);
                return Ok(APIResponse<TutorSearchPagedResponse>.Success(result, "Tìm kiếm gia sư thành công."));
            }
            catch (ArgumentException ex)
            {
                return BadRequest(APIResponse<object>.Fail(ex.Message, 400));
            }
            catch (Exception ex)
            {
                return StatusCode(500, APIResponse<object>.Fail($"Đã xảy ra lỗi khi tìm kiếm gia sư: {ex.Message}.", 500));
            }
        }
    }
}
