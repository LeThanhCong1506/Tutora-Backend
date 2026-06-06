using System.ComponentModel.DataAnnotations;

namespace MV.DomainLayer.DTO.RequestModel
{
    public class UpdateTutorSubjectsRequest
    {
        [Required(ErrorMessage = "Subject list is required")]
        [MinLength(1, ErrorMessage = "You must select at least one subject")]
        public List<int> SubjectIds { get; set; } = new();

        [Range(0, double.MaxValue, ErrorMessage = "Hourly rate must be a positive number")]
        public decimal HourlyRate { get; set; }

        public List<TutorSubjectGradePriceRequest> SubjectGradePrices { get; set; } = new();
    }
}
