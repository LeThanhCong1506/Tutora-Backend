namespace MV.DomainLayer.Configuration
{
    public class GoogleGeminiSettings
    {
        public const string SectionName = "GoogleGemini";

        public string ApiKey { get; set; } = string.Empty;
        public string Model { get; set; } = "gemini-pro";
        public int MaxOutputTokens { get; set; } = 2048;
        public float Temperature { get; set; } = 0.7f;
    }
}
