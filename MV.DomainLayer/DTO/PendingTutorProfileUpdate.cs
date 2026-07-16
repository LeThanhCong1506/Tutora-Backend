using MV.DomainLayer.DTO.RequestModel;

namespace MV.DomainLayer.DTO
{
    /// <summary>
    /// Bản đề xuất chỉnh sửa hồ sơ của 1 Tutor đã Active, chờ Admin duyệt.
    /// KHÔNG phải EF entity — chỉ serialize/deserialize JSON và lưu trong Redis
    /// (xem ITutorProfileUpdateStagingService). Tutorprofile thật không đổi cho
    /// đến khi Admin approve.
    /// </summary>
    public class PendingTutorProfileUpdate
    {
        public string TutorId { get; set; } = string.Empty;

        /// <summary>pending | approved | rejected — xem TutorProfileUpdateStatus</summary>
        public string Status { get; set; } = string.Empty;

        public string? Headline { get; set; }

        public string? TeachingAreaCity { get; set; }

        public string? TeachingAreaDistrict { get; set; }

        public string? Bio { get; set; }

        public string? Education { get; set; }

        public double? Gpa { get; set; }

        public double? GpaScale { get; set; }

        public string? Experience { get; set; }

        public string? VideoIntroUrl { get; set; }

        public List<TutorSubjectGradePriceRequest>? SubjectGradePrices { get; set; }

        public DateTime SubmittedAt { get; set; }
    }
}
