namespace MV.DomainLayer.DTO.RequestModel
{
    /// <summary>
    /// Request model for updating tutor pricing information
    /// </summary>
    public class UpdateTutorPricingRequest
    {
        /// <summary>
        /// Hourly rate in VND (50,000 - 2,000,000)
        /// </summary>
        public decimal HourlyRate { get; set; }

        /// <summary>
        /// Trial lesson price in VND (must be less than or equal to HourlyRate)
        /// </summary>
        public decimal? TrialLessonPrice { get; set; }

        /// <summary>
        /// Whether the tutor allows price negotiation
        /// </summary>
        public bool AllowPriceNegotiation { get; set; }

        /// <summary>
        /// New pricing model: price per tutor + subject + grade level.
        /// If omitted, legacy HourlyRate updates existing active prices.
        /// </summary>
        public List<TutorSubjectGradePriceRequest> SubjectGradePrices { get; set; } = new();
    }

    public class TutorSubjectGradePriceRequest
    {
        public int SubjectId { get; set; }

        public int GradeLevelId { get; set; }

        public decimal PricePerHour { get; set; }

        public string? Currency { get; set; }

        public bool IsActive { get; set; } = true;
    }
}
