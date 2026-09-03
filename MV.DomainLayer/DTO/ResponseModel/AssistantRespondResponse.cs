using System.Text.Json;

namespace MV.DomainLayer.DTO.ResponseModel;

public class AssistantRespondResponse
{
    public string Reply { get; set; } = string.Empty;

    /// <summary>"tutor" | "faq" | "off_topic".</summary>
    public string Intent { get; set; } = "tutor";

    public List<AssistantCardDto> Cards { get; set; } = new();

    /// <summary>
    /// Filter tutora-ai trả về sau khi merge — FE giữ hộ rồi gửi lại ở CurrentFilters lượt sau.
    /// JsonElement (không phải DTO có field) vì .NET chỉ chuyển tiếp, không đọc: xem chú thích
    /// ở AssistantRespondRequest.CurrentFilters.
    /// </summary>
    public JsonElement? Filters { get; set; }

    public bool AiRanked { get; set; }

    public List<string> Suggestions { get; set; } = new();

    public string? SessionId { get; set; }

    /// <summary>Entity memory trả về cho FE giữ & gửi lại lượt sau (như Filters).</summary>
    public List<AssistantShownTutorOut> ShownTutors { get; set; } = new();
}

public class AssistantShownTutorOut
{
    public string TutorId { get; set; } = string.Empty;
    public string? Name { get; set; }
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

