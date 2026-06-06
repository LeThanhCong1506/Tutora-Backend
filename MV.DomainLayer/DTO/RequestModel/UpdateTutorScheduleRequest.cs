using System.ComponentModel.DataAnnotations;

namespace MV.DomainLayer.DTO.RequestModel
{
    // Mô tả chi tiết một khung giờ rảnh
    public class WeeklyFreeTimeSlot
    {
        [Range(0, 6, ErrorMessage = "Day of week must be 0 (Sun) to 6 (Sat).")]
        public int DayOfWeek { get; set; }

        [Required(ErrorMessage = "Please provide the start time.")]
        [RegularExpression(@"^([01]\d|2[0-3]):([0-5]\d)$", ErrorMessage = "Use HH:mm format (e.g., 08:30).")]
        public string StartTime { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please provide the end time.")]
        [RegularExpression(@"^([01]\d|2[0-3]):([0-5]\d)$", ErrorMessage = "Use HH:mm format (e.g., 17:00).")]
        public string EndTime { get; set; } = string.Empty;
    }

    // Yêu cầu cập nhật toàn bộ lịch trình trong tuần
    public class UpdateTutorScheduleRequest
    {
        [Required(ErrorMessage = "The list of free time slots cannot be empty.")]
        public List<WeeklyFreeTimeSlot> ListOfFreeTimeSlots { get; set; } = new();
    }
}
