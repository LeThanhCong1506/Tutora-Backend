using System.ComponentModel.DataAnnotations;
using MV.DomainLayer.Attributes;

namespace MV.DomainLayer.DTO.RequestModel
{
    /// <summary>
    /// Request model for creating a tutor availability slot
    /// </summary>
    public class CreateAvailabilityRequest
    {
        /// <summary>
        /// Day of week (1 = Monday, 2 = Tuesday, ..., 7 = Sunday)
        /// </summary>
        [Required(ErrorMessage = "Day of week is required")]
        [Range(1, 7, ErrorMessage = "Day of week must be between 1 (Monday) and 7 (Sunday)")]
        public int Dayofweek { get; set; }

        /// <summary>
        /// Start time in format HH:mm (e.g., "09:00")
        /// </summary>
        [Required(ErrorMessage = "Start time is required")]
        [RegularExpression(@"^([01]?[0-9]|2[0-3]):[0-5][0-9]$", ErrorMessage = "Start time must be in HH:mm format")]
        // [TimeRange("07:00", "21:00", ErrorMessage = "Start time must be between 07:00 and 21:00")]
        public string Starttime { get; set; } = string.Empty;

        /// <summary>
        /// End time in format HH:mm (e.g., "11:00")
        /// </summary>
        [Required(ErrorMessage = "End time is required")]
        [RegularExpression(@"^(([01]?[0-9]|2[0-3]):[0-5][0-9]|24:00)$", ErrorMessage = "End time must be in HH:mm format (00:00 – 24:00).")]
        // [TimeRange("07:00", "21:00", ErrorMessage = "End time must be between 07:00 and 21:00")]
        public string Endtime { get; set; } = string.Empty;
    }
}
