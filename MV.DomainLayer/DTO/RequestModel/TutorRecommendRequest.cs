using MV.DomainLayer.Enums;

namespace MV.DomainLayer.DTO.RequestModel
{
    public class TutorRecommendRequest
    {
        public int? SubjectId { get; set; }
        public int? GradeLevelId { get; set; }
        public string? GradeLevel { get; set; }
        public string? TeachingMode { get; set; }
        public string? City { get; set; }
        public string? District { get; set; }
        public decimal? MinRate { get; set; }
        public decimal? MaxRate { get; set; }
        // Lọc giới tính gia sư (Male=1 | Female=2).
        public Gender? Gender { get; set; }
        public string? Query { get; set; }
        public int TopK { get; set; } = 10;

        // Lịch rảnh — dữ liệu có cấu trúc, lọc CỨNG ở SQL 
        public List<int>? AvailableDaysOfWeek { get; set; }
        public TimeOnly? AvailableFrom { get; set; }
        public TimeOnly? AvailableTo { get; set; }
    }
}
