using System.ComponentModel.DataAnnotations;

namespace MV.DomainLayer.DTO.RequestModel;

public class ScheduleItemRequest
{
    /// <summary>Day of week (1 = Monday, 2 = Tuesday, ..., 7 = Sunday)</summary>
    [Range(1, 7, ErrorMessage = "Day of week must be between 1 (Monday) and 7 (Sunday)")]
    public int DayOfWeek { get; set; }

    [Required]
    [RegularExpression(@"^([01]?[0-9]|2[0-3]):[0-5][0-9]$", ErrorMessage = "StartTime must be HH:mm")]
    public string StartTime { get; set; } = null!;

    [Required]
    [RegularExpression(@"^([01]?[0-9]|2[0-3]):[0-5][0-9]$", ErrorMessage = "EndTime must be HH:mm")]
    public string EndTime { get; set; } = null!;
}
