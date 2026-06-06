using System.ComponentModel.DataAnnotations;

namespace MV.DomainLayer.DTO.RequestModel;

public class ScheduleItemRequest
{
    [Range(0, 6, ErrorMessage = "DayOfWeek must be 0 (Sunday) to 6")]
    public int DayOfWeek { get; set; }

    [Required]
    [RegularExpression(@"^([01]?[0-9]|2[0-3]):[0-5][0-9]$", ErrorMessage = "StartTime must be HH:mm")]
    public string StartTime { get; set; } = null!;

    [Required]
    [RegularExpression(@"^([01]?[0-9]|2[0-3]):[0-5][0-9]$", ErrorMessage = "EndTime must be HH:mm")]
    public string EndTime { get; set; } = null!;
}
