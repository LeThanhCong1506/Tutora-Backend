namespace MV.DomainLayer.Configuration;

/// <summary>
/// Google OAuth2 settings — used for Google Login (ID token validation).
/// </summary>
public class GoogleSettings
{
    public const string SectionName = "Google";

    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
}
