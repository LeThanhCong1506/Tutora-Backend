namespace MV.DomainLayer.Settings;

/// <summary>
/// Cấu hình kết nối tới service AI (tutora-ai / FastAPI).
/// </summary>
public class TutorAiSettings
{
    public const string SectionName = "TutorAi";

    public string BaseUrl { get; set; } = null!;

    /// <summary>API key gửi qua header X-API-Key tới tutora-ai.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Timeout cho streaming /solve — dài hơn vì câu trả lời stream lâu.</summary>
    public int StreamTimeoutSeconds { get; set; } = 120;
}
