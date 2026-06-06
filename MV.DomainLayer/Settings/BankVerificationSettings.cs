namespace MV.DomainLayer.Settings;

public class BankVerificationSettings
{
    public const string SectionName = "BankVerification";

    // Verification code settings
    public int VerificationCodeLength { get; set; } = 6;
    public int VerificationCodeExpiryHours { get; set; } = 24;
    public int MaxVerificationAttempts { get; set; } = 5;

    // Rate limiting
    public int RateLimitMaxRequests { get; set; } = 3;
    public int RateLimitWindowHours { get; set; } = 24;

    // Micro-deposit
    public decimal MicroDepositAmount { get; set; } = 1000m;
    public string MicroDepositDescription { get; set; } = "AGORA VERIFY"; // Đổi TUTORA

    // Webhook security
    public string[] AllowedWebhookIps { get; set; } = [];
    public bool EnableWebhookSignatureVerification { get; set; } = true;
    public bool EnableWebhookIpWhitelist { get; set; } = false;

    // Webhook status values (no hardcoded strings)
    public string WebhookStatusCompleted { get; set; } = "completed";
    public string WebhookStatusSuccess { get; set; } = "success";
    public string WebhookStatusFailed { get; set; } = "failed";

    // Logging
    public bool MaskSensitiveData { get; set; } = true;
    public int AccountNumberVisibleDigits { get; set; } = 4;

    // Audit trail
    public bool EnableAuditTrail { get; set; } = true;
}
