namespace MV.DomainLayer.DTO.ResponseModel
{
    /// <summary>
    /// Response model for tutor pricing information
    /// </summary>
    public class TutorPricingResponse
    {
        /// <summary>
        /// Hourly rate in VND
        /// </summary>
        public decimal? HourlyRate { get; set; }

        /// <summary>
        /// Trial lesson price in VND
        /// </summary>
        public decimal? TrialLessonPrice { get; set; }

        /// <summary>
        /// Whether the tutor allows price negotiation
        /// </summary>
        public bool? AllowPriceNegotiation { get; set; }

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

        public string Currency { get; set; } = "VND";

        public bool IsActive { get; set; }
    }
}
