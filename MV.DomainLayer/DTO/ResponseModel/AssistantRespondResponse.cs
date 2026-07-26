namespace MV.DomainLayer.DTO.ResponseModel;

public class AssistantRespondResponse
{
    public string Reply { get; set; } = string.Empty;

    /// <summary>"tutor" | "faq" | "off_topic".</summary>
    public string Intent { get; set; } = "tutor";

    public List<AssistantCardDto> Cards { get; set; } = new();

    public AssistantFiltersOut Filters { get; set; } = new();

    public bool AiRanked { get; set; }

    public List<string> Suggestions { get; set; } = new();

    public string? SessionId { get; set; }
}

public class AssistantCardDto
{
    public string TutorId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public bool IsBestMatch { get; set; }
    public double? PricePerHour { get; set; }
    public double? Rating { get; set; }
    public int? TotalReviews { get; set; }
    public List<string> Highlights { get; set; } = new();
    public string ProfileUrl { get; set; } = string.Empty;
    public string CtaLabel { get; set; } = "Xem chi tiết";
}

public class AssistantFiltersOut
{
    public double? MinRate { get; set; }
    public double? MaxRate { get; set; }
    public string? TutorGender { get; set; }
    public int? SubjectId { get; set; }
    public int? DesiredCount { get; set; }
}
