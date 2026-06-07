namespace MV.DomainLayer.DTO.ResponseModel
{
    public class TutorPackageResponse
    {
        public int PackageId { get; set; }

        public string TutorId { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        /// <summary>1 = flexible, 2 = fixed.</summary>
        public int PackageType { get; set; }


        public bool IsActive { get; set; }

        public List<TutorPackageFixedSlotResponse> FixedSlots { get; set; } = new();
    }

    public class TutorPackageFixedSlotResponse
    {
        public int FixedSlotId { get; set; }

        public int DayOfWeek { get; set; }

        public string StartTime { get; set; } = string.Empty;

        public string EndTime { get; set; } = string.Empty;
    }
}
