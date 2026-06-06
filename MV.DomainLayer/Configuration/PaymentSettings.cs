namespace MV.DomainLayer.Configuration;

public class PaymentSettings
{
    public const string SectionName = "Payment";

    public string ClientId { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string ChecksumKey { get; set; } = "";
    public string PayoutClientId { get; set; } = "";
    public string PayoutApiKey { get; set; } = "";
    public string PayoutChecksumKey { get; set; } = "";
    public string ReturnUrl { get; set; } = "";
    public string CancelUrl { get; set; } = "";
    public string WebhookUrl { get; set; } = "";
}
