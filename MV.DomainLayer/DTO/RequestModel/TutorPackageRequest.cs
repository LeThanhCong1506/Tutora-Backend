using System.ComponentModel.DataAnnotations;

namespace MV.DomainLayer.DTO.RequestModel
{
    public class CreateTutorPackageRequest
    {
        public string Name { get; set; } = string.Empty;

        /// <summary>1 = flexible, 2 = fixed.</summary>
        public int PackageType { get; set; } = 1;


        public List<TutorPackageFixedSlotRequest> FixedSlots { get; set; } = new();
    }

    public class TutorPackageFixedSlotRequest
    {
        /// <summary>Day of week (1 = Monday, 2 = Tuesday, ..., 7 = Sunday)</summary>
        [Range(1, 7, ErrorMessage = "Day of week must be between 1 (Monday) and 7 (Sunday)")]
        public int DayOfWeek { get; set; }

        public string StartTime { get; set; } = string.Empty;

        public string EndTime { get; set; } = string.Empty;
    }
}
