namespace MV.DomainLayer.DTO.ResponseModel
{
    /// <summary>
    /// Public tutor schedule payload including weekly availability and active packages.
    /// </summary>
    public class TutorScheduleResponse
    {
        public string TutorId { get; set; } = string.Empty;
        public List<TutorAvailabilityResponse> Availabilities { get; set; } = new();
        public List<TutorPackageResponse> Packages { get; set; } = new();
    }
}
