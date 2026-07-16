namespace MV.DomainLayer.DTO.ResponseModel
{
    /// <summary>
    /// Response model for tutor pricing information
    /// </summary>
    public class TutorPricingResponse
    {
        public List<TutorSubjectGradePriceResponse> SubjectGradePrices { get; set; } = new();
    }

    public class TutorSubjectGradePriceResponse
    {
        public int Id { get; set; }

        public int SubjectId { get; set; }

        public string? SubjectName { get; set; }

        public int GradeLevelId { get; set; }

        public string? GradeLevelName { get; set; }

        public decimal PricePerHour { get; set; }

        public int DurationMinutesPerSession { get; set; }

        public int SessionsPerWeek { get; set; }

        public string Currency { get; set; } = "VND";

        public bool IsActive { get; set; }

        /// <summary>
        /// Môn học này còn được cung cấp trên hệ thống không (Subject.IsActive).
        /// false = Admin đã ngừng dùng môn → FE nên cảnh báo/khóa dòng này, tutor cần bỏ
        /// trước khi lưu (validation sẽ từ chối nếu giữ lại môn đã ngừng dùng).
        /// </summary>
        public bool SubjectIsActive { get; set; } = true;

        /// <summary>
        /// Khối lớp này còn được cung cấp trên hệ thống không (Gradelevel.IsActive).
        /// false = Admin đã ngừng dùng khối → FE nên cảnh báo/khóa dòng này, tutor cần bỏ
        /// trước khi lưu (validation sẽ từ chối nếu giữ lại khối đã ngừng dùng).
        /// </summary>
        public bool GradeLevelIsActive { get; set; } = true;
    }
}
