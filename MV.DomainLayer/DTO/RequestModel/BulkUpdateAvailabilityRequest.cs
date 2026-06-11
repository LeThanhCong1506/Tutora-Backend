using System.ComponentModel.DataAnnotations;

namespace MV.DomainLayer.DTO.RequestModel
{
    public class BulkUpdateAvailabilityRequest
    {
        [Required(ErrorMessage = "Availabilities list is required")]
        [MinLength(1, ErrorMessage = "At least one availability slot is required")]
        public List<UpdateAvailabilityItem> Availabilities { get; set; } = new();
    }

    public class UpdateAvailabilityItem
    {
        [Required(ErrorMessage = "AvailabilityId is required")]
        public int Availabilityid { get; set; }

        [Required(ErrorMessage = "Day of week is required")]
        [Range(1, 7, ErrorMessage = "Day of week must be between 1 (Monday) and 7 (Sunday)")]
        public int Dayofweek { get; set; }

        [Required(ErrorMessage = "Start time is required")]
        [RegularExpression(@"^([01]?[0-9]|2[0-3]):[0-5][0-9]$", ErrorMessage = "Start time must be in HH:mm format")]
        public string Starttime { get; set; } = string.Empty;

        [Required(ErrorMessage = "End time is required")]
        [RegularExpression(@"^(([01]?[0-9]|2[0-3]):[0-5][0-9]|24:00)$", ErrorMessage = "End time must be in HH:mm format (00:00 – 24:00).")]
        public string Endtime { get; set; } = string.Empty;
    }
}
