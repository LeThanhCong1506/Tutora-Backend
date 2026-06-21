namespace MV.DomainLayer.DTO.ResponseModel
{
    public class ProfileCompletionResponse
    {
        public int CompletedSections { get; set; }
        public int TotalSections { get; set; }
        public bool CanSubmit { get; set; }
        public string ProfileStatus { get; set; } = string.Empty;
        public List<ProfileSection> Sections { get; set; } = [];
    }

    public class ProfileSection
    {
        public string Key { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public bool IsComplete { get; set; }
        public string? MissingReason { get; set; }
    }
}
