using System.Text.Json.Serialization;

namespace MV.DomainLayer.DTO.RequestModel;

public record ZaloPayCreateOrderRequest(int BookingId);

public record ZaloPayConfirmRequest(int BookingId, string ZpTransToken);

/// <summary>
/// ZaloPay webhook payload (form-encoded).
/// </summary>
public class ZaloPayWebhookPayload
{
    [JsonPropertyName("app_id")]
    public int AppId { get; set; }

    [JsonPropertyName("app_trans_id")]
    public string? AppTransId { get; set; }

    [JsonPropertyName("zp_trans_id")]
    public long ZpTransId { get; set; }

    [JsonPropertyName("amount")]
    public long Amount { get; set; }

    [JsonPropertyName("server_time")]
    public long ServerTime { get; set; }

    [JsonPropertyName("channel")]
    public int Channel { get; set; }

    [JsonPropertyName("merchant_user_id")]
    public string? MerchantUserId { get; set; }

    [JsonPropertyName("return_code")]
    public int ReturnCode { get; set; }

    [JsonPropertyName("mac")]
    public string? Mac { get; set; }
}
