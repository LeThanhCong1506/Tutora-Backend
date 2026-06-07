namespace MV.DomainLayer.DTO.RequestModel
{
    /// <summary>
    /// Request model for updating tutor pricing information
    /// </summary>
    public class UpdateTutorPricingRequest
    {
        /// <summary>
        /// Pricing model: price per subject + grade level.
        /// </summary>
        public List<TutorSubjectGradePriceRequest> SubjectGradePrices { get; set; } = new();
    }

    public class TutorSubjectGradePriceRequest
    {
        public int SubjectId { get; set; }

        public int GradeLevelId { get; set; }

        public decimal PricePerHour { get; set; }

        public int DurationMinutesPerSession { get; set; } = 60;

        public int SessionsPerWeek { get; set; } = 1;

        public string? Currency { get; set; }

        public bool IsActive { get; set; } = true;
    }
}
