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
        public int DayOfWeek { get; set; }

        public string StartTime { get; set; } = string.Empty;

        public string EndTime { get; set; } = string.Empty;
    }
}
